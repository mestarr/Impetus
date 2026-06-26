using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using PicoGK;

namespace Impetus.Geometry;

/// <summary>
/// Export PicoGK meshes to interchange formats. PicoGK only writes STL; 3MF is
/// built here so slicers (e.g. Anycubic Kobra S1) get explicit millimeter units.
/// </summary>
public static class MeshExport
{
    public readonly record struct ThreeMfPart(string Name, Mesh Mesh);

    public static void SaveMeshFiles(Mesh msh, string strStlPath, string strThreeMfPath, string strTitle)
    {
        msh.SaveToStlFile(strStlPath);
        SaveToThreeMfFile(strThreeMfPath, strTitle, new ThreeMfPart(strTitle, msh));
    }

    public static void SaveAssemblyMeshFiles(
        Mesh mshCombined,
        IReadOnlyList<ThreeMfPart> aoParts,
        string strStlPath,
        string strThreeMfPath,
        string strTitle)
    {
        mshCombined.SaveToStlFile(strStlPath);
        SaveToThreeMfFile(strThreeMfPath, strTitle, aoParts);
    }

    public static void SaveToThreeMfFile(Mesh msh, string strPath, string strTitle)
        => SaveToThreeMfFile(strPath, strTitle, new ThreeMfPart(strTitle, msh));

    public static void SaveToThreeMfFile(string strPath, string strTitle, params ThreeMfPart[] aoParts)
        => SaveToThreeMfFile(strPath, strTitle, (IReadOnlyList<ThreeMfPart>)aoParts);

    public static void SaveToThreeMfFile(string strPath, string strTitle, IReadOnlyList<ThreeMfPart> aoParts)
    {
        if (aoParts.Count == 0)
            throw new ArgumentException("At least one 3MF part is required.", nameof(aoParts));

        WriteThreeMfArchive(strPath, BuildModelXml(strTitle, aoParts));
    }

    static string BuildModelXml(string strTitle, IReadOnlyList<ThreeMfPart> aoParts)
    {
        var sb = new StringBuilder(4096);
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append("<model unit=\"millimeter\" xml:lang=\"und-US\" xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\" xmlns:m=\"http://schemas.microsoft.com/3dmanufacturing/material/2015/02\">");

        string strNow = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        sb.Append("<metadata name=\"Title\">").Append(EscapeXml(strTitle)).Append("</metadata>");
        sb.Append("<metadata name=\"Application\">Impetus computational thruster design</metadata>");
        sb.Append("<metadata name=\"CreationDate\">").Append(strNow).Append("</metadata>");
        sb.Append("<metadata name=\"Designer\">Impetus</metadata>");
        sb.Append("<metadata name=\"Description\">Rocket engine display geometry; units millimeters; not for hot fire in plastic.</metadata>");

        sb.Append("<resources>");
        for (int i = 0; i < aoParts.Count; i++)
        {
            int nId = i + 1;
            ThreeMfPart oPart = aoParts[i];
            sb.Append("<object id=\"").Append(nId).Append("\" type=\"model\" name=\"")
                .Append(EscapeXml(oPart.Name)).Append("\"><mesh><vertices>");
            AppendVertices(sb, oPart.Mesh);
            sb.Append("</vertices><triangles>");
            AppendTriangles(sb, oPart.Mesh);
            sb.Append("</triangles></mesh></object>");
        }
        sb.Append("</resources><build>");
        for (int i = 0; i < aoParts.Count; i++)
            sb.Append("<item objectid=\"").Append(i + 1).Append("\"/>");
        sb.Append("</build></model>");
        return sb.ToString();
    }

    static void AppendVertices(StringBuilder sb, Mesh msh)
    {
        int nVerts = msh.nVertexCount();
        for (int i = 0; i < nVerts; i++)
        {
            Vector3 vec = msh.vecVertexAt(i);
            sb.Append("<vertex x=\"")
                .Append(vec.X.ToString("G9", CultureInfo.InvariantCulture))
                .Append("\" y=\"")
                .Append(vec.Y.ToString("G9", CultureInfo.InvariantCulture))
                .Append("\" z=\"")
                .Append(vec.Z.ToString("G9", CultureInfo.InvariantCulture))
                .Append("\"/>");
        }
    }

    static void AppendTriangles(StringBuilder sb, Mesh msh)
    {
        int nTris = msh.nTriangleCount();
        for (int i = 0; i < nTris; i++)
        {
            Triangle tri = msh.oTriangleAt(i);
            sb.Append("<triangle v1=\"")
                .Append(tri.A)
                .Append("\" v2=\"")
                .Append(tri.B)
                .Append("\" v3=\"")
                .Append(tri.C)
                .Append("\"/>");
        }
    }

    static string EscapeXml(string str)
        => str.Replace("&", "&amp;", StringComparison.Ordinal)
              .Replace("<", "&lt;", StringComparison.Ordinal)
              .Replace(">", "&gt;", StringComparison.Ordinal)
              .Replace("\"", "&quot;", StringComparison.Ordinal);

    static void WriteThreeMfArchive(string strPath, string strModelXml)
    {
        if (File.Exists(strPath))
            File.Delete(strPath);

        using FileStream oFile = File.Create(strPath);
        using var zip = new ZipArchive(oFile, ZipArchiveMode.Create);

        AddZipEntry(zip, "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="model" ContentType="application/vnd.ms-package.3dmanufacturing-3dmodel+xml"/>
            </Types>
            """);

        AddZipEntry(zip, "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Target="/3D/3dmodel.model" Id="rel0" Type="http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel"/>
            </Relationships>
            """);

        AddZipEntry(zip, "3D/3dmodel.model", strModelXml);
    }

    static void AddZipEntry(ZipArchive zip, string strName, string strContent)
    {
        ZipArchiveEntry oEntry = zip.CreateEntry(strName, CompressionLevel.Optimal);
        using StreamWriter writer = new(oEntry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(strContent.Trim());
    }
}
