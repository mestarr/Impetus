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
static class MeshExport
{
    public static void SaveMeshFiles(Mesh msh, string strStlPath, string strThreeMfPath, string strTitle)
    {
        msh.SaveToStlFile(strStlPath);
        SaveToThreeMfFile(msh, strThreeMfPath, strTitle);
    }

    public static void SaveToThreeMfFile(Mesh msh, string strPath, string strTitle)
    {
        int nVerts = msh.nVertexCount();
        int nTris = msh.nTriangleCount();

        var sbVerts = new StringBuilder(nVerts * 48);
        for (int i = 0; i < nVerts; i++)
        {
            Vector3 vec = msh.vecVertexAt(i);
            sbVerts.Append("<vertex x=\"")
                .Append(vec.X.ToString("G9", CultureInfo.InvariantCulture))
                .Append("\" y=\"")
                .Append(vec.Y.ToString("G9", CultureInfo.InvariantCulture))
                .Append("\" z=\"")
                .Append(vec.Z.ToString("G9", CultureInfo.InvariantCulture))
                .Append("\"/>");
        }

        var sbTris = new StringBuilder(nTris * 32);
        for (int i = 0; i < nTris; i++)
        {
            Triangle tri = msh.oTriangleAt(i);
            sbTris.Append("<triangle v1=\"")
                .Append(tri.A)
                .Append("\" v2=\"")
                .Append(tri.B)
                .Append("\" v3=\"")
                .Append(tri.C)
                .Append("\"/>");
        }

        WriteThreeMfArchive(strPath, BuildModelXml(strTitle, sbVerts.ToString(), sbTris.ToString()));
    }

    static string BuildModelXml(string strTitle, string strVertices, string strTriangles)
    {
        var sb = new StringBuilder(strVertices.Length + strTriangles.Length + 1024);
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append("<model unit=\"millimeter\" xml:lang=\"und-US\" xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\" xmlns:m=\"http://schemas.microsoft.com/3dmanufacturing/material/2015/02\">");
        sb.Append("<metadata name=\"Title\">").Append(EscapeXml(strTitle)).Append("</metadata>");
        sb.Append("<metadata name=\"Application\">Impetus computational thruster design</metadata>");
        sb.Append("<resources><object id=\"1\" type=\"model\"><mesh><vertices>");
        sb.Append(strVertices);
        sb.Append("</vertices><triangles>");
        sb.Append(strTriangles);
        sb.Append("</triangles></mesh></object></resources>");
        sb.Append("<build><item objectid=\"1\"/></build>");
        sb.Append("</model>");
        return sb.ToString();
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
