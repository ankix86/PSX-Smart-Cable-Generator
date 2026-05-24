using UnityEngine;

/// <summary>
/// Built-in look presets for PSX Smart Cable Generator. Values are applied to <see cref="PSCableGenerator"/> via <see cref="PSCablePresetUtility"/>.
/// </summary>
public enum PSCablePreset
{
    Custom = 0,
    Ethernet = 1,
    PowerCable = 2,
    IndustrialThick = 3,
    HangingWire = 4,
}

/// <summary>
/// Default tuning per preset (editor-time only; serialized fields on the component remain authoritative after apply).
/// </summary>
public static class PSCablePresetUtility
{
    public static void Apply(PSCableGenerator g, PSCablePreset preset)
    {
        if (g == null || preset == PSCablePreset.Custom)
            return;

        switch (preset)
        {
            case PSCablePreset.Ethernet:
                g.RibbonMode = false;
                g.CableThickness = 0.024f;
                g.RadialSegments = 5;
                g.PathResolution = 18;
                g.SagStrength = 0.35f;
                g.CurveTension = 0.5f;
                g.ThicknessJitterPSX = 0.08f;
                break;
            case PSCablePreset.PowerCable:
                g.RibbonMode = false;
                g.CableThickness = 0.044f;
                g.RadialSegments = 6;
                g.PathResolution = 22;
                g.SagStrength = 0.45f;
                g.CurveTension = 0.55f;
                g.ThicknessJitterPSX = 0.06f;
                break;
            case PSCablePreset.IndustrialThick:
                g.RibbonMode = false;
                g.CableThickness = 0.09f;
                g.RadialSegments = 6;
                g.PathResolution = 16;
                g.SagStrength = 0.5f;
                g.CurveTension = 0.45f;
                g.ThicknessJitterPSX = 0.05f;
                break;
            case PSCablePreset.HangingWire:
                g.RibbonMode = true;
                g.CableThickness = 0.008f;
                g.RadialSegments = 4;
                g.PathResolution = 28;
                g.SagStrength = 0.65f;
                g.CurveTension = 0.5f;
                g.ThicknessJitterPSX = 0.12f;
                break;
        }

        g.CableRadius = g.CableThickness * 0.5f;
        g.RibbonWidth = g.CableThickness;
    }
}
