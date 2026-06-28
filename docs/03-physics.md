# 3. Physics — Every Equation in the Model

All formulas below are implemented 1:1 in `src/Impetus/Physics/`. Units are SI
unless stated. Primary references:

- **[Sutton]** Sutton & Biblarz, *Rocket Propulsion Elements*, 7th–9th ed., ch. 3, 5, 8
- **[H&H]** Huzel & Huang, *Modern Engineering for Design of Liquid-Propellant
  Rocket Engines*, ch. 4
- **[Bartz]** D. R. Bartz, "A Simple Equation for Rapid Estimation of Rocket
  Nozzle Convective Heat Transfer Coefficients", *Jet Propulsion*, 1957
- **[Rao]** G. V. R. Rao, "Exhaust Nozzle Contour for Optimum Thrust",
  *Jet Propulsion*, 1958

Symbols: \(p_c\) chamber (stagnation) pressure, \(p_e\) exit static pressure,
\(p_a\) ambient pressure, \(T_c\) chamber temperature, \(\gamma\) ratio of
specific heats, \(R_s\) specific gas constant, \(M\) Mach number,
\(A_t, A_e\) throat/exit areas, \(\varepsilon = A_e/A_t\) expansion ratio,
\(\dot m\) mass flow, \(F\) thrust, \(g_0 = 9.80665\) m/s².

---

## 3.1 Combustion gas model (`GasModel.cs`)

v1 deliberately avoids running chemical equilibrium and instead carries a
table of representative product-gas properties per propellant pair, taken from
standard references at typical mixture ratios and \(p_c \sim 2\) MPa:

| Pair | O/F | \(T_c\) [K] | \(\mathcal{M}\) [g/mol] | \(\gamma\) | L* [m] |
|---|---|---|---|---|---|
| LOX / RP-1 (kerosene) | 2.3 | 3570 | 21.9 | 1.22 | 1.0 |
| LOX / CH₄ (methane) | 3.4 | 3530 | 21.3 | 1.23 | 0.9 |
| LOX / LH₂ (hydrogen) | 5.5 | 3300 | 12.0 | 1.26 | 0.75 |
| N₂O / Ethanol | 4.5 | 2980 | 24.5 | 1.22 | 1.2 |

Derived quantities:

\[ R_s = \frac{R_0}{\mathcal{M}}, \qquad
   c_p = \frac{\gamma R_s}{\gamma - 1} \]

Transport properties for the thermal model use Bartz-era engineering
approximations [Sutton ch. 8]:

\[ \mu(T) \approx 1.184\times10^{-7}\,\mathcal{M}_{g/mol}^{0.5}\,T^{0.6}
   \;\;[\text{Pa·s}], \qquad
   \Pr \approx \frac{4\gamma}{9\gamma - 5} \]

**Error budget:** frozen-flow textbook values typically land within ~2–5 % of
a full CEA equilibrium calculation for Isp-class quantities at these
conditions. That is acceptable for sizing; the roadmap upgrades this to
CEA-derived tables varying with O/F and \(p_c\).

---

## 3.2 Isentropic flow relations (`IsentropicFlow.cs`)

Quasi-1D, calorically perfect gas [Sutton ch. 3]:

**Stagnation ratios at Mach \(M\):**

\[ \frac{T_0}{T} = 1 + \frac{\gamma-1}{2}M^2, \qquad
   \frac{p_0}{p} = \left(1 + \frac{\gamma-1}{2}M^2\right)^{\gamma/(\gamma-1)} \]

**Area–Mach relation** (the workhorse — every CFD mesh station and every
thermal station inverts this):

\[ \frac{A}{A^*} = \frac{1}{M}\left[\frac{2}{\gamma+1}
   \left(1+\frac{\gamma-1}{2}M^2\right)\right]^{\frac{\gamma+1}{2(\gamma-1)}} \]

Inverted numerically by 200-step bisection on the chosen branch (subsonic
\(M\in(0,1)\), supersonic \(M\in(1,100)\)); monotonic on each branch, so
bisection is unconditionally safe.

**Characteristic velocity** — a pure property of the propellant gas, measures
combustion performance independent of the nozzle:

\[ c^* = \frac{\sqrt{\gamma R_s T_c}}
   {\gamma\left(\frac{2}{\gamma+1}\right)^{\frac{\gamma+1}{2(\gamma-1)}}} \]

**Mass flow through a choked throat:**

\[ \dot m = \frac{p_c A_t}{c^*} \]

**Thrust coefficient** — what the *nozzle* adds on top of \(c^*\):

\[ C_F = \sqrt{\frac{2\gamma^2}{\gamma-1}
   \left(\frac{2}{\gamma+1}\right)^{\frac{\gamma+1}{\gamma-1}}
   \left[1-\left(\frac{p_e}{p_c}\right)^{\frac{\gamma-1}{\gamma}}\right]}
   \;+\; \varepsilon\,\frac{p_e - p_a}{p_c} \]

**Ideal exhaust velocity:**

\[ v_e = \sqrt{\frac{2\gamma}{\gamma-1} R_s T_c
   \left[1-\left(\frac{p_e}{p_c}\right)^{\frac{\gamma-1}{\gamma}}\right]} \]

**Thrust and specific impulse:**

\[ F = C_F\,p_c\,A_t = \dot m v_e + (p_e - p_a)A_e, \qquad
   I_{sp} = \frac{F}{\dot m\,g_0} \]

---

## 3.3 Sizing sequence (`EngineSizing.cs`)

Given the spec, in order:

1. **Expansion ratio.**
   - If the spec fixes \(\varepsilon\): exit Mach from the supersonic branch of
     the area–Mach relation, then \(p_e\) from the pressure ratio.
   - If `expansionRatio = 0` (optimal): set \(p_e = p_a\), get exit Mach from

     \[ M_e = \sqrt{\frac{2}{\gamma-1}\left[\left(\frac{p_c}{p_e}\right)^{\frac{\gamma-1}{\gamma}}-1\right]} \]

     then \(\varepsilon = A/A^*(M_e)\). Optimal expansion maximizes \(C_F\) at
     the design ambient pressure (∂CF/∂ε = 0 exactly when \(p_e = p_a\)).

2. **Throat.** \(C_F\) from §3.2, then

   \[ A_t = \frac{F_{target}}{C_F\,p_c}, \qquad R_t = \sqrt{A_t/\pi},
      \qquad R_e = R_t\sqrt{\varepsilon} \]

3. **Flows.** \(\dot m = p_c A_t / c^*\), split by mixture ratio
   \(\phi =\) O/F:

   \[ \dot m_f = \frac{\dot m}{1+\phi}, \qquad \dot m_{ox} = \dot m - \dot m_f \]

4. **Vacuum performance.** Same \(C_F\) expression with \(p_a = 0\) →
   \(I_{sp,vac}\).

5. **Chamber.** Two classic empirical anchors [H&H §4.3, Sutton ch. 8]:
   - **Characteristic length** \(L^* = V_c / A_t\) — total chamber volume per
     unit throat area, a stay-time proxy. Taken per-propellant from the table
     (e.g. 1.0 m for LOX/kerosene). Gives \(V_c = L^* A_t\).
   - **Contraction ratio** \(CR = A_c/A_t\) (spec input, default 4), with a
     small-engine floor from the Huzel & Huang correlation (fig. 4-9 class):

     \[ CR_{min} = 8.0\,D_{t,cm}^{-0.6} + 1.25, \qquad
        CR_{used} = \max(CR_{spec},\, CR_{min}) \]

     Small throats need proportionally fatter chambers — without this floor
     the \(L^*\) volume rule produces absurd pencil-shaped chambers for
     thrusters under a few kN. Then \(R_c = R_t\sqrt{CR_{used}}\).

   The cylinder length then follows from the volume budget: the convergent
   section is approximated as a 30° cone frustum for volume purposes,

   \[ V_{conv} = \frac{\pi h}{3}(R_c^2 + R_c R_t + R_t^2),\quad
      h = \frac{R_c - R_t}{\tan 30°} \]

   \[ L_{cyl} = \text{clamp}\!\left(\frac{V_c - V_{conv}}{\pi R_c^2},\;
      0.8\,R_c,\; 4.4\,R_c\right) \]

   The lower bound keeps geometry sane if the volume budget is already met by
   the convergent cone alone; the upper bound (2.2 chamber diameters) caps
   slenderness — past that, extra residence time buys nothing and the report's
   applied contraction ratio tells you the model intervened.

6. **Bell length.** Fraction \(k\) (default 0.8) of an equivalent 15° conical
   nozzle [Rao; H&H §4.4]:

   \[ L_{bell} = k\,\frac{R_e - R_t}{\tan 15°} \]

7. **Injector orifices.** Showerhead with stiffness
   \(\Delta p = 0.20\,p_c\) (industry rule of thumb: stiff enough to decouple
   feed system from chamber acoustics), discharge coefficient \(C_d = 0.7\)
   (sharp-edged orifice), incompressible orifice equation per propellant:

   \[ A_{inj} = \frac{\dot m_i}{C_d\sqrt{2\rho_i\,\Delta p}} \]

   Fixed orifice diameters (0.6 mm fuel / 0.8 mm ox) → orifice counts.

---

## 3.4 Thermal model (`ThermalModel.cs`)

### Bartz correlation

Hot-gas-side convective coefficient along the contour [Bartz 1957]:

\[ h_g = \underbrace{\frac{0.026}{D_t^{0.2}}
   \frac{\mu^{0.2} c_p}{\Pr^{0.6}}
   \left(\frac{p_c}{c^*}\right)^{0.8}
   \left(\frac{D_t}{r_{curv}}\right)^{0.1}}_{\text{constant for the engine}}
   \;\left(\frac{A_t}{A}\right)^{0.9}\,\sigma \]

with the local-property correction

\[ \sigma = \left[\tfrac{1}{2}\frac{T_w}{T_c}
   \left(1+\tfrac{\gamma-1}{2}M^2\right)+\tfrac{1}{2}\right]^{-0.68}
   \left(1+\tfrac{\gamma-1}{2}M^2\right)^{-0.12} \]

- \(r_{curv}\): mean throat curvature radius; Impetus uses the average of its
  two contour arcs, \(\bar r = \frac{1.5 + 0.382}{2} R_t \approx 0.94 R_t\).
- \(T_w\): hot-wall design temperature, fixed at **800 K** (typical for a
  regen-cooled copper-alloy liner).
- Local \(M\) at each contour station from the area–Mach inversion
  (subsonic branch before the throat, supersonic after).

### Heat flux and total load

Adiabatic-wall (recovery) temperature with recovery factor
\(r = \Pr^{1/3}\) (turbulent):

\[ T_{aw} = T_c\,\frac{1 + r\frac{\gamma-1}{2}M^2}{1+\frac{\gamma-1}{2}M^2},
   \qquad q = h_g\,(T_{aw} - T_w) \]

Total load integrates strip-wise over the revolved wall surface
(frustum strips):

\[ Q = \sum_i q_i\,\pi(r_i + r_{i-1})
   \sqrt{\Delta z_i^2 + \Delta r_i^2} \]

### 1D regenerative cooling march (`RegenSolver.cs`)

The solver walks the **channel path** (injector manifold → nozzle collector,
matching `ThrusterBuilder` Z limits) with coupled hot-side Bartz, CuCrZr liner
conduction, and Gnielinski tube-side convection. Coolant temperature and
pressure are updated per segment:

\[ q = \frac{T_{aw} - T_c}{1/h_g + t/k + 1/h_c}, \qquad
   \Delta T_c = \frac{q\,\Delta A}{\dot m_f\,c_{p,fuel}} \]

Channel friction uses Darcy–Weisbach with a Blasius-style \(f(Re)\).

\[ \Delta T_{coolant} = T_{c,out} - T_{c,in}, \qquad
   v_{cool} = \frac{\dot m_f}{\rho_f\,N_{ch}\,A_{ch}} \]

**What this is for:** honest bulk coolant ΔT, peak wall temperature, and channel
Δp — plus auto channel sizing (`SizeChannelsForWallTemp`) when peak wall exceeds
the CuCrZr screening limit. Kerolox at 1 kN may still fail the fuel ΔT gate;
run `iterate` to lower O/F or change propellant.

---

## 3.5 Wall contour construction (`NozzleContour.cs`)

The inner wall is a single continuous polyline in the (z, r) half-plane,
z = 0 at the injector face. Standard five-element construction [H&H §4.4]:

```
 r ▲
   │←Lcyl→│
 Rc├──────╮ (2) fillet R=0.5·Rc
   │ (1)   ╲
   │        ╲ (3) 30° cone
   │         ╲
   │          ╰╮ (4) arc 1.5·Rt          (6) Rao parabola
   │           ╰─╮ ╭───────────────────────────────── Re
 Rt│         (5)╰─╯←arc 0.382·Rt, ends at angle θn
   └───────────────┴────────────────────────────────────► z
                 throat                              exit
```

1. **Cylinder** at \(R_c\), length \(L_{cyl}\).
2. **Entry fillet**, radius \(0.5R_c\), turning the wall from horizontal to
   the convergence angle (30°).
3. **Convergent cone**, half-angle 30° (classic compromise: short chamber vs
   gentle acceleration).
4. **Upstream throat arc**, radius \(1.5R_t\) [Rao's recommendation], tangent
   to both the cone and the throat.
5. **Downstream throat arc**, radius \(0.382R_t\) [Rao], from the throat up to
   the parabola take-off angle \(\theta_n\).
6. **Rao bell as a quadratic Bézier** (the standard "parabolic approximation"
   to Rao's method-of-characteristics optimum, [H&H fig. 4-15/16]):
   - start point **N** = end of arc (5), wall angle \(\theta_n\);
   - end point **E** = \((z_t + L_{bell},\, R_e)\), wall angle \(\theta_e\);
   - control point **Q** = intersection of the tangents at N and E, which
     guarantees the curve leaves at \(\theta_n\) and arrives at \(\theta_e\):

     \[ z_Q = \frac{R_e - R_N + z_N\tan\theta_n - z_E\tan\theta_e}
        {\tan\theta_n - \tan\theta_e}, \qquad
        r_Q = R_N + (z_Q - z_N)\tan\theta_n \]

   - \(\theta_n, \theta_e\) come from a lookup of Rao's design charts for an
     80 % bell, linearly interpolated in \(\varepsilon\):

     | ε | 3.5 | 4 | 5 | 10 | 20 | 30 | 40 | 50 |
     |---|---|---|---|---|---|---|---|---|
     | θn [°] | 20.5 | 21 | 22 | 24.5 | 27 | 28.2 | 29 | 29.5 |
     | θe [°] | 14.5 | 14 | 13 | 11 | 9 | 8.3 | 8 | 7.5 |

The throat axial position is **derived, not chosen**: the radial drop
\(R_c → R_t\) is partitioned between fillet, cone and throat arc, each
contributing its exact geometric share — so all five elements meet tangentially
(C¹ continuous wall, no kinks for the flow or the CFD mesh).

**Axisymmetry note:** the same contour drives the 3D geometry (revolved) and
the 2D CFD mesh, so the simulated shape *is* the printed shape of the gas path
— by construction, with zero translation error.

---

## 3.6 Validity envelope

The model is trustworthy (few-percent level on global quantities) when:

- \(p_c\) ≳ 7 bar (well-choked, ideal-gas assumptions hold),
- thrust between ~50 N and ~50 kN (the empirical anchors L*, CR, Δp rules were
  calibrated in this class),
- conventional bell nozzles, \(\varepsilon\) within the chart range 3.5–50,
- steady state.

Outside that envelope the math still runs, but treat results as qualitative.
