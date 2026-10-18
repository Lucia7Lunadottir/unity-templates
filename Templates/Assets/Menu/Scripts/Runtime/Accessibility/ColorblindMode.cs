namespace PG.Accessibility
{
    public enum ColorblindMode
    {
        None = 0,

        // Anomalous Trichromacy (LUT_Unity_16, 256x16)
        Protanomaly = 1,     // Red_weak
        Deuteranomaly = 2,   // Green_weak
        Tritanomaly = 3,     // Blue_weak

        // Dichromatic View
        Protanopia = 4,      // Red_blind
        Deuteranopia = 5,    // Green_blind
        Tritanopia = 6,      // Blue_blind

        // Monochromatic View
        Achromatopsia = 7,        // Monochromacy
        BlueConeMonochromacy = 8  // Blue Cone Monochromacy
    }
}
