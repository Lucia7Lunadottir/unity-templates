using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public class MenuGraphicsSettings : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private TMP_Dropdown _screenResolutionDropdown;
    [SerializeField] private TMP_Dropdown _fullscreenDropdown;
    [SerializeField] private TMP_Dropdown _volumetricFogDropdown;
    [SerializeField] private TMP_Dropdown _presetDropdown; // <--- ƒÓ·‡‚ÎÂÌÓ
    [SerializeField] private Slider _shadowSlider;
    [SerializeField] private Slider _textureResolutionSlider;
    [SerializeField] private Slider _lodSlider;
    [SerializeField] private Slider _antiAliasingSlider;
    [SerializeField] private Slider _renderScaleSlider;
    [SerializeField] private Slider _vSyncSlider;

    private UniversalRenderPipelineAsset _asset;
    private Resolution[] _resolutions;
    public static bool volumetricFogActive = true;

    // --- PlayerPrefs keys ---
    const string PP_RES_INDEX = "gfx.res.index";
    const string PP_FULLSCREEN = "gfx.fullscreen.mode";
    const string PP_VFOG = "gfx.vfog";
    const string PP_SHADOW = "gfx.shadow.cascades";
    const string PP_TEXTURE = "gfx.tex.mipmaplimit";
    const string PP_LOD = "gfx.lod.bias";
    const string PP_AA = "gfx.msaa";
    const string PP_RS = "gfx.render.scale";
    const string PP_VSYNC = "gfx.vsync";
    const string PP_PRESET = "gfx.preset";

    public enum Preset { Low = 0, Medium = 1, High = 2, Ultra = 3, Auto = 4 }

    [Serializable]
    public class SettingsData
    {
        public int resolutionIndex;
        public int fullscreenMode;
        public bool volumetricFog;
        public int shadowCascades;
        public int textureMipmapLimit;
        public float lodBias;
        public int msaa;
        public float renderScale;
        public int vSync;
        public Preset preset;
    }

    private SettingsData _data = new SettingsData();

    // ---------------- Lifecycle ----------------
    void Awake()
    {
        _asset = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
        _resolutions = Screen.resolutions;

        RegisterUI();
        PopulateDropdowns();

        if (PlayerPrefs.HasKey(PP_PRESET))
        {
            LoadFromPrefs();
        }
        else
        {
            _data = MakeFromCurrentRuntime();
            ApplyPreset(Preset.Auto, true, true);
        }

        SyncUIFromData();
    }

    void OnDestroy()
    {
        UnRegisterUI();
    }

    // ---------------- UI Setup ----------------
    private void PopulateDropdowns()
    {
        // –¿«–≈ÿ≈Õ»ﬂ
        _screenResolutionDropdown.ClearOptions();
        var resOptions = new List<string>();
        foreach (var r in _resolutions)
        {
            var hz = Mathf.RoundToInt((float)r.refreshRateRatio.value);
            resOptions.Add($"{r.width}x{r.height} ({hz}Hz)");
        }
        _screenResolutionDropdown.AddOptions(resOptions);

        // ‘”ÀÀ— –»Õ
        _fullscreenDropdown.ClearOptions();
        _fullscreenDropdown.AddOptions(new List<string> { "Fullscreen", "Windowed", "Maximized" });


        // œ–≈—≈“€
        _presetDropdown.ClearOptions();
        _presetDropdown.AddOptions(new List<string> { "Low", "Medium", "High", "Ultra", "Auto" });
    }

    private void RegisterUI()
    {
        _screenResolutionDropdown.onValueChanged.AddListener(SetResolution);
        _fullscreenDropdown.onValueChanged.AddListener(SetFullscreen);
        _volumetricFogDropdown.onValueChanged.AddListener(SetVolumetricFogQuality);
        _shadowSlider.onValueChanged.AddListener(SetShadowQuality);
        _textureResolutionSlider.onValueChanged.AddListener(SetTextureQuality);
        _lodSlider.onValueChanged.AddListener(SetLODQuality);
        _antiAliasingSlider.onValueChanged.AddListener(SetAntiAliasing);
        _renderScaleSlider.onValueChanged.AddListener(SetRenderScale);
        _vSyncSlider.onValueChanged.AddListener(SetVSync);
        _presetDropdown.onValueChanged.AddListener(SetPreset); // <--- ÕÓ‚˚È listener
    }

    private void UnRegisterUI()
    {
        _screenResolutionDropdown.onValueChanged.RemoveAllListeners();
        _fullscreenDropdown.onValueChanged.RemoveAllListeners();
        _volumetricFogDropdown.onValueChanged.RemoveAllListeners();
        _shadowSlider.onValueChanged.RemoveAllListeners();
        _textureResolutionSlider.onValueChanged.RemoveAllListeners();
        _lodSlider.onValueChanged.RemoveAllListeners();
        _antiAliasingSlider.onValueChanged.RemoveAllListeners();
        _renderScaleSlider.onValueChanged.RemoveAllListeners();
        _vSyncSlider.onValueChanged.RemoveAllListeners();
        _presetDropdown.onValueChanged.RemoveAllListeners();
    }

    // ---------------- œËÏÂÌÂÌËÂ ----------------
    private void ApplyAll()
    {
        SetResolution(_data.resolutionIndex, false);
        SetFullscreen(_data.fullscreenMode, false);
        SetShadowQuality(_data.shadowCascades, false);
        SetTextureQuality(_data.textureMipmapLimit, false);
        SetLODQuality(_data.lodBias, false);
        SetAntiAliasingIndex(FromMSAAtoIndex(_data.msaa), false);
        SetRenderScale(_data.renderScale, false);
        SetVSync(_data.vSync, false);
        SetVolumetricFogQuality(_data.volumetricFog ? 1 : 0, false);
    }

    private SettingsData MakeFromCurrentRuntime()
    {
        var data = new SettingsData();
        data.resolutionIndex = ClosestResolutionIndex(Screen.currentResolution);
        data.fullscreenMode = ToFullscreenIndex(Screen.fullScreenMode);
        data.volumetricFog = volumetricFogActive;
        data.shadowCascades = _asset ? _asset.shadowCascadeCount : 1;
        data.textureMipmapLimit = QualitySettings.globalTextureMipmapLimit;
        data.lodBias = QualitySettings.lodBias;
        data.msaa = GetCurrentMSAA();
        data.renderScale = _asset ? _asset.renderScale : 1f;
        data.vSync = QualitySettings.vSyncCount;
        data.preset = Preset.Auto;
        return data;
    }

    private int ClosestResolutionIndex(Resolution current)
    {
        if (_resolutions == null || _resolutions.Length == 0) return 0;
        int idx = 0;
        int bestScore = int.MaxValue;
        for (int i = 0; i < _resolutions.Length; i++)
        {
            int dx = _resolutions[i].width - current.width;
            int dy = _resolutions[i].height - current.height;
            int score = dx * dx + dy * dy;
            if (score < bestScore) { bestScore = score; idx = i; }
        }
        return idx;
    }

    // ---------------- —Óı‡ÌÂÌËÂ ----------------
    private void SaveToPrefs()
    {
        PlayerPrefs.SetInt(PP_RES_INDEX, _data.resolutionIndex);
        PlayerPrefs.SetInt(PP_FULLSCREEN, _data.fullscreenMode);
        PlayerPrefs.SetInt(PP_VFOG, _data.volumetricFog ? 1 : 0);
        PlayerPrefs.SetInt(PP_SHADOW, _data.shadowCascades);
        PlayerPrefs.SetInt(PP_TEXTURE, _data.textureMipmapLimit);
        PlayerPrefs.SetFloat(PP_LOD, _data.lodBias);
        PlayerPrefs.SetInt(PP_AA, _data.msaa);
        PlayerPrefs.SetFloat(PP_RS, _data.renderScale);
        PlayerPrefs.SetInt(PP_VSYNC, _data.vSync);
        PlayerPrefs.SetInt(PP_PRESET, (int)_data.preset);
        PlayerPrefs.Save();
    }

    private void LoadFromPrefs()
    {
        _data.resolutionIndex = PlayerPrefs.GetInt(PP_RES_INDEX, 0);
        _data.fullscreenMode = PlayerPrefs.GetInt(PP_FULLSCREEN, 0);
        _data.volumetricFog = PlayerPrefs.GetInt(PP_VFOG, 1) == 1;
        _data.shadowCascades = PlayerPrefs.GetInt(PP_SHADOW, 2);
        _data.textureMipmapLimit = PlayerPrefs.GetInt(PP_TEXTURE, 1);
        _data.lodBias = PlayerPrefs.GetFloat(PP_LOD, 1f);
        _data.msaa = PlayerPrefs.GetInt(PP_AA, 4);
        _data.renderScale = PlayerPrefs.GetFloat(PP_RS, 1f);
        _data.vSync = PlayerPrefs.GetInt(PP_VSYNC, 1);
        _data.preset = (Preset)PlayerPrefs.GetInt(PP_PRESET, (int)Preset.Auto);
    }

    // ---------------- œÂÒÂÚ˚ ----------------
    private void SetPreset(int index)
    {
        var preset = (Preset)index;
        ApplyPreset(preset, true, true);
    }

    private void ApplyPreset(Preset preset, bool applyNow = true, bool saveAfter = false)
    {
        _data.preset = preset;

        if (preset == Preset.Auto)
            preset = DetectPresetFromHardware();

        switch (preset)
        {
            case Preset.Low:
                _data.shadowCascades = 1;
                _data.textureMipmapLimit = 3;
                _data.lodBias = 0.7f;
                _data.msaa = 0;
                _data.renderScale = 0.8f;
                _data.vSync = 0;
                _data.volumetricFog = false;
                break;
            case Preset.Medium:
                _data.shadowCascades = 2;
                _data.textureMipmapLimit = 2;
                _data.lodBias = 1.0f;
                _data.msaa = 2;
                _data.renderScale = 1.0f;
                _data.vSync = 1;
                _data.volumetricFog = true;
                break;
            case Preset.High:
                _data.shadowCascades = 4;
                _data.textureMipmapLimit = 1;
                _data.lodBias = 1.4f;
                _data.msaa = 4;
                _data.renderScale = 1.1f;
                _data.vSync = 1;
                _data.volumetricFog = true;
                break;
            case Preset.Ultra:
                _data.shadowCascades = 4;
                _data.textureMipmapLimit = 0;
                _data.lodBias = 1.7f;
                _data.msaa = 8;
                _data.renderScale = 1.2f;
                _data.vSync = 1;
                _data.volumetricFog = true;
                break;
        }

        if (applyNow) { ApplyAll(); SyncUIFromData(); }
        if (saveAfter) SaveToPrefs();
    }

    private Preset DetectPresetFromHardware()
    {
        int score = 0;
        score += Mathf.Clamp(SystemInfo.graphicsMemorySize / 1024, 0, 4);
        score += (SystemInfo.graphicsShaderLevel >= 45) ? 2 : (SystemInfo.graphicsShaderLevel >= 35 ? 1 : 0);
        if (SystemInfo.supportsComputeShaders) score += 2;
        score += Mathf.Clamp(SystemInfo.processorCount / 2, 0, 4);
        score += Mathf.Clamp(SystemInfo.systemMemorySize / 4096, 0, 4);
        if (score >= 14) return Preset.Ultra;
        if (score >= 10) return Preset.High;
        if (score >= 6) return Preset.Medium;
        return Preset.Low;
    }

    // ---------------- UI Sync ----------------
    private void SyncUIFromData()
    {
        UnRegisterUI();
        _screenResolutionDropdown.value = Mathf.Clamp(_data.resolutionIndex, 0, _resolutions.Length - 1);
        _fullscreenDropdown.value = Mathf.Clamp(_data.fullscreenMode, 0, 2);
        _volumetricFogDropdown.value = _data.volumetricFog ? 1 : 0;
        _shadowSlider.value = _data.shadowCascades;
        _textureResolutionSlider.value = _data.textureMipmapLimit;
        _lodSlider.value = _data.lodBias;
        _antiAliasingSlider.value = FromMSAAtoIndex(_data.msaa);
        _renderScaleSlider.value = _data.renderScale;
        _vSyncSlider.value = _data.vSync;
        _presetDropdown.value = (int)_data.preset;
        RegisterUI();
    }

    // ---------------- Handlers ----------------
    public void SetShadowQuality(float value) => SetShadowQuality((int)value, true);
    private void SetShadowQuality(int cascades, bool save)
    {
        if (_asset) _asset.shadowCascadeCount = cascades;
        _data.shadowCascades = cascades;
        if (save) SaveToPrefs();
    }

    public void SetVSync(float value) => SetVSync((int)value, true);
    private void SetVSync(int v, bool save)
    {
        QualitySettings.vSyncCount = v;
        _data.vSync = v;
        if (save) SaveToPrefs();
    }

    public void SetLODQuality(float value) => SetLODQuality(value, true);

    private void SetLODQuality(float value, bool save = true)
    {
        QualitySettings.lodBias = value;
        _data.lodBias = value;
        if (save) SaveToPrefs();
    }

    public void SetAntiAliasing(float value) => SetAntiAliasingIndex((int)value, true);
    private void SetAntiAliasingIndex(int index, bool save)
    {
        int msaa = FromIndexToMSAA(index);
        Screen.SetMSAASamples(msaa);
        _data.msaa = msaa;
        if (save) SaveToPrefs();
    }

    public void SetRenderScale(float value) => SetRenderScale(value, true);
    public void SetRenderScale(float value, bool save = true)
    {
        if (_asset) _asset.renderScale = value;
        _data.renderScale = value;
        if (save) SaveToPrefs();
    }

    public void SetTextureQuality(float value) => SetTextureQuality((int)value, true);
    private void SetTextureQuality(int mipLimit, bool save)
    {
        QualitySettings.globalTextureMipmapLimit = mipLimit;
        _data.textureMipmapLimit = mipLimit;
        if (save) SaveToPrefs();
    }

    public void SetResolution(int value) => SetResolution(value, true);
    private void SetResolution(int value, bool save)
    {
        var r = _resolutions[Mathf.Clamp(value, 0, _resolutions.Length - 1)];
        Screen.SetResolution(r.width, r.height, Screen.fullScreenMode);
        _data.resolutionIndex = value;
        if (save) SaveToPrefs();
    }

    public void SetFullscreen(int value) => SetFullscreen(value, true);
    private void SetFullscreen(int value, bool save)
    {
        switch (value)
        {
            case 0: Screen.fullScreenMode = FullScreenMode.FullScreenWindow; break;
            case 1: Screen.fullScreenMode = FullScreenMode.Windowed; break;
            case 2: Screen.fullScreenMode = FullScreenMode.MaximizedWindow; break;
        }
        _data.fullscreenMode = value;
        if (save) SaveToPrefs();
    }

    public void SetVolumetricFogQuality(int value) => SetVolumetricFogQuality(value, true);
    private void SetVolumetricFogQuality(int value, bool save)
    {
        volumetricFogActive = value == 1;
        _data.volumetricFog = volumetricFogActive;
        if (save) SaveToPrefs();
    }

    // ---------------- Helpers ----------------
    private static int FromIndexToMSAA(int index)
    {
        switch (index)
        {
            case 0: return 0;
            case 1: return 2;
            case 2: return 4;
            case 3: return 8;
            default: return 0;
        }
    }

    private static int FromMSAAtoIndex(int msaa)
    {
        switch (msaa)
        {
            case 0: return 0;
            case 2: return 1;
            case 4: return 2;
            case 8: return 3;
            default: return 0;
        }
    }

    private static int ToFullscreenIndex(FullScreenMode mode)
    {
        switch (mode)
        {
            case FullScreenMode.FullScreenWindow: return 0;
            case FullScreenMode.Windowed: return 1;
            case FullScreenMode.MaximizedWindow: return 2;
            default: return 0;
        }
    }

    private static int GetCurrentMSAA()
    {
        return QualitySettings.antiAliasing > 0 ? QualitySettings.antiAliasing : 0;
    }
}
