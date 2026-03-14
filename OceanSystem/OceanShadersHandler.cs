using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

using System.Runtime.InteropServices;

public class OceanShadersHandler : MonoBehaviour
{

    [System.Serializable]
    public struct DisplaySpectrumSettings
    {
        [Range(0, 1)]
        public float scale;
        public float windSpeed;
        public float windDirection;
        public float fetch;
        [Range(0, 1)]
        public float spreadBlend;
        [Range(0, 1)]
        public float swell;
        public float peakEnhancement;
        public float shortWavesFade;
    }
    public struct SpectrumSettings
    {
        public float scale;
        public float angle;
        public float spreadBlend;
        public float swell;
        public float alpha;
        public float peakOmega;
        public float gamma;
        public float shortWavesFade;

        public SpectrumSettings(DisplaySpectrumSettings display, WaveSettings waveSettings) {
            scale = display.scale;
            angle = display.windDirection / 180 * Mathf.PI;
            spreadBlend = display.spreadBlend;
            swell = Mathf.Clamp(display.swell, 0.01f, 1);
            // JonswapAlpha 0.076f * Mathf.Pow(g * fetch / windSpeed / windSpeed, -0.22f);
            alpha = 0.076f * Mathf.Pow(waveSettings.g * display.fetch / display.windSpeed / display.windSpeed, -0.22f);
            // JonswapPeakFrequency 22 * Mathf.Pow(windSpeed * fetch / g / g, -0.33f);
            peakOmega = 22 * Mathf.Pow(display.windSpeed * display.fetch / waveSettings.g / waveSettings.g, -0.33f);
            gamma = display.peakEnhancement;
            shortWavesFade = display.shortWavesFade;
        }
    }
    [System.Serializable]
    public struct WaveSettings 
    {
        public float g;
        public float depth;
        [Range(0, 1)]
        public float lambda;
        public DisplaySpectrumSettings local, swell;
    }

    public int textureSize = 128; // must be power of 2
    public int noiseSeed, textureThreadGroups, logSize;

    public float[] cascadesLengths = new float[3] {
        250, 17, 5
    };

    public OceanGeometry oceanGeometry;

    public ComputeShader GaussianNoise, InitialSpectrum, TimeDependentSpectrum, FFT, WavesTexturesMerger, DisplacementReader;

    public WaveSettings waveSettings;

    ComputeBuffer spectrumSettingsBuffer;

    ComputeBuffer inputPositionsBuffer, outputPositionsBuffer, outputSlopesBuffer;
    const int maxPhysicsObjects = 32;
    int displacementReaderThreads;

    Vector2[] inputPositions = new Vector2[maxPhysicsObjects];
    float[] outputPositions = new float[maxPhysicsObjects];
    Vector2[] outputSlopes = new Vector2[maxPhysicsObjects];

    int sampleHeightsID;

    public RenderTexture NoiseTexture, PrecomputedFFTTexture;
    RenderTexture PrecomputedDataTexture;

    public SpectrumSettings[] spectrums;
    public WavesCascade[] cascades;

    readonly int PROP_ID_PRECOMPUTED_DATA = Shader.PropertyToID("PrecomputedData");
    readonly int PROP_ID_BUFFER0 = Shader.PropertyToID("Buffer0");
    readonly int PROP_ID_BUFFER1 = Shader.PropertyToID("Buffer1");
    readonly int PROP_ID_SIZE = Shader.PropertyToID("Size");
    readonly int PROP_ID_STEP = Shader.PropertyToID("Step");

    public static RenderTexture CreateRenderTexture(
        int sizex, int sizey, 
        RenderTextureFormat format = RenderTextureFormat.RGFloat, 
        bool useMips = false, 
        FilterMode filterMode = FilterMode.Trilinear, 
        int ansioLevel = 6, 
        bool autoGenerateMips = false
    )
    {
        RenderTexture rt = new RenderTexture(sizex, sizey, 0,
            format, RenderTextureReadWrite.Linear);
        rt.useMipMap = useMips;
        rt.autoGenerateMips = autoGenerateMips;
        rt.anisoLevel = ansioLevel;
        rt.filterMode = filterMode;
        rt.wrapMode = TextureWrapMode.Repeat;
        rt.enableRandomWrite = true;
        rt.Create();
        return rt;
    }

    void GenerateNoise(int seed) {
        int noiseInitID = GaussianNoise.FindKernel("GenerateNoise");
        GaussianNoise.SetTexture(noiseInitID, "Result", NoiseTexture);
        GaussianNoise.SetInt("Seed", seed);

        GaussianNoise.Dispatch(noiseInitID, textureThreadGroups, textureThreadGroups, 1);
    }
    void PrecomputeTwiddleFactorsAndInputIndices() {
        int KERNEL_PRECOMPUTE = FFT.FindKernel("PrecomputeTwiddleFactorsAndInputIndices");
        FFT.SetInt("Size", textureSize);
        FFT.SetTexture(KERNEL_PRECOMPUTE, "PrecomputeBuffer", PrecomputedFFTTexture);

        FFT.Dispatch(KERNEL_PRECOMPUTE, logSize, textureThreadGroups / 2, 1);
    }

    void InitialiseCascades()
    {
        for (int i = 0; i < cascadesLengths.Length; i++) {
            float boundary1 = i > 0 ? 2 * Mathf.PI / cascadesLengths[i] * 6f : 0.0001f;
            float boundary2 = (i+1) < cascadesLengths.Length ? 2 * Mathf.PI / cascadesLengths[i+1] * 6f : 9999;

            cascades[i].CalculateInitials(cascadesLengths[i], boundary1, boundary2);
            Shader.SetGlobalFloat($"LengthScale{i}", cascadesLengths[i]);
        }
    }

    void InitDisplacementReader() {
        sampleHeightsID = DisplacementReader.FindKernel("SampleHeights");

        inputPositionsBuffer = new ComputeBuffer(maxPhysicsObjects, sizeof(float) * 2);
        outputPositionsBuffer = new ComputeBuffer(maxPhysicsObjects, sizeof(float));
        outputSlopesBuffer = new ComputeBuffer(maxPhysicsObjects, sizeof(float) * 2);

        DisplacementReader.SetBuffer(sampleHeightsID, "InputPositions", inputPositionsBuffer);
        DisplacementReader.SetBuffer(sampleHeightsID, "OutputHeights", outputPositionsBuffer);
        DisplacementReader.SetBuffer(sampleHeightsID, "OutputSlope", outputSlopesBuffer);

        for (int i = 0; i < cascadesLengths.Length; i++) {
            DisplacementReader.SetFloat($"Length_c{i}", cascadesLengths[i]);
        }

        DisplacementReader.GetKernelThreadGroupSizes(sampleHeightsID, out uint groupSizeX, out _, out _);

        displacementReaderThreads = (int)groupSizeX;
    }

    void Start() {
        OnDestroy();

        spectrumSettingsBuffer = new ComputeBuffer(2, Marshal.SizeOf<SpectrumSettings>());

        spectrums = new SpectrumSettings[2] {
            new SpectrumSettings(waveSettings.local, waveSettings),
            new SpectrumSettings(waveSettings.swell, waveSettings)
        };

        NoiseTexture = CreateRenderTexture(textureSize, textureSize, RenderTextureFormat.RGFloat, false, FilterMode.Point, 1);

        textureThreadGroups = textureSize / 8;
        logSize = (int)Mathf.Log(textureSize, 2);

        GenerateNoise(noiseSeed);

        cascades = new WavesCascade[cascadesLengths.Length];

        for (int i = 0; i < cascadesLengths.Length; i++) 
            cascades[i] = new WavesCascade(this);

        InitialiseCascades();

        PrecomputedFFTTexture = CreateRenderTexture(logSize, textureSize, RenderTextureFormat.ARGBFloat, false, FilterMode.Point, 1);

        PrecomputeTwiddleFactorsAndInputIndices();

        InitDisplacementReader();

        oceanGeometry.Initialize(this);
    }
    
    void FixedUpdate() {
        if (!enabled) return;

        for (int i = 0; i < cascadesLengths.Length; i++)
            cascades[i].CalculateWavesAtTime(Time.fixedTime);

        oceanGeometry.UpdateGeometry();

        int activeObjects = Mathf.Min(WaterPhysics.objects.Count, maxPhysicsObjects);

        for (int i = 0; i < activeObjects; i++) {
            Vector3 pos = WaterPhysics.objects[i].transform.position;
            inputPositions[i] = new Vector2(pos.x, pos.z);
        }

        if (activeObjects > 0) {
            for (int i = 0; i < cascadesLengths.Length; i++) {
                DisplacementReader.SetTexture(sampleHeightsID, $"Displacement_c{i}", cascades[i].Displacement);
                DisplacementReader.SetTexture(sampleHeightsID, $"Derivatives_c{i}", cascades[i].Derivatives);
            }

            inputPositionsBuffer.SetData(inputPositions);

            int threadGroupsX = Mathf.CeilToInt((float)activeObjects / displacementReaderThreads);

            DisplacementReader.Dispatch(sampleHeightsID, threadGroupsX, 1, 1);

            outputPositionsBuffer.GetData(outputPositions);
            outputSlopesBuffer.GetData(outputSlopes);

            for (int i = 0; i < activeObjects; i++) {
                Vector2 slope = outputSlopes[i];
                WaterPhysics.objects[i].currentHeight = outputPositions[i] + oceanGeometry.oceanLevel;
                WaterPhysics.objects[i].currentNormal = new Vector3(-slope.x, 1, -slope.y).normalized;
            }
        }
    }

    public void IFFT2D(RenderTexture input, RenderTexture buffer, bool outputToInput = true, bool scale = false, bool permute = true)
    {
        bool pingPong = false;
        
        int KERNEL_HORIZONTAL_STEP_IFFT_PING = FFT.FindKernel("HorizontalStepIFFT_Ping");
        int KERNEL_HORIZONTAL_STEP_IFFT_PONG = FFT.FindKernel("HorizontalStepIFFT_Pong");

        int KERNEL_VERTICAL_STEP_IFFT_PING = FFT.FindKernel("VerticalStepIFFT_Ping");
        int KERNEL_VERTICAL_STEP_IFFT_PONG = FFT.FindKernel("VerticalStepIFFT_Pong");

        int KERNEL_SCALE = FFT.FindKernel("Scale");
        int KERNEL_PERMUTE = FFT.FindKernel("Permute");

        FFT.SetTexture(KERNEL_HORIZONTAL_STEP_IFFT_PING, PROP_ID_PRECOMPUTED_DATA, PrecomputedFFTTexture);
        FFT.SetTexture(KERNEL_HORIZONTAL_STEP_IFFT_PING, PROP_ID_BUFFER0, input);
        FFT.SetTexture(KERNEL_HORIZONTAL_STEP_IFFT_PING, PROP_ID_BUFFER1, buffer);

        FFT.SetTexture(KERNEL_HORIZONTAL_STEP_IFFT_PONG, PROP_ID_PRECOMPUTED_DATA, PrecomputedFFTTexture);
        FFT.SetTexture(KERNEL_HORIZONTAL_STEP_IFFT_PONG, PROP_ID_BUFFER0, input);
        FFT.SetTexture(KERNEL_HORIZONTAL_STEP_IFFT_PONG, PROP_ID_BUFFER1, buffer);

        for (int i = 0; i < logSize; i++)
        {
            pingPong = !pingPong;
            FFT.SetInt(PROP_ID_STEP, i);
            if (pingPong)
                FFT.Dispatch(KERNEL_HORIZONTAL_STEP_IFFT_PING, textureThreadGroups, textureThreadGroups, 1);
            else
                FFT.Dispatch(KERNEL_HORIZONTAL_STEP_IFFT_PONG, textureThreadGroups, textureThreadGroups, 1);
        }

        FFT.SetTexture(KERNEL_VERTICAL_STEP_IFFT_PING, PROP_ID_PRECOMPUTED_DATA, PrecomputedFFTTexture);
        FFT.SetTexture(KERNEL_VERTICAL_STEP_IFFT_PING, PROP_ID_BUFFER0, input);
        FFT.SetTexture(KERNEL_VERTICAL_STEP_IFFT_PING, PROP_ID_BUFFER1, buffer);

        FFT.SetTexture(KERNEL_VERTICAL_STEP_IFFT_PONG, PROP_ID_PRECOMPUTED_DATA, PrecomputedFFTTexture);
        FFT.SetTexture(KERNEL_VERTICAL_STEP_IFFT_PONG, PROP_ID_BUFFER0, input);
        FFT.SetTexture(KERNEL_VERTICAL_STEP_IFFT_PONG, PROP_ID_BUFFER1, buffer);

        for (int i = 0; i < logSize; i++)
        {
            pingPong = !pingPong;
            FFT.SetInt(PROP_ID_STEP, i);
            if (pingPong)
                FFT.Dispatch(KERNEL_VERTICAL_STEP_IFFT_PING, textureThreadGroups, textureThreadGroups, 1);
            else
                FFT.Dispatch(KERNEL_VERTICAL_STEP_IFFT_PONG, textureThreadGroups, textureThreadGroups, 1);
        }

        if (pingPong && outputToInput)
        {
            Graphics.Blit(buffer, input);
        }

        if (!pingPong && !outputToInput)
        {
            Graphics.Blit(input, buffer);
        }

        if (permute)
        {
            FFT.SetInt(PROP_ID_SIZE, textureSize);
            FFT.SetTexture(KERNEL_PERMUTE, PROP_ID_BUFFER0, outputToInput ? input : buffer);
            FFT.Dispatch(KERNEL_PERMUTE, textureThreadGroups, textureThreadGroups, 1);
        }
        
        if (scale)
        {
            FFT.SetInt(PROP_ID_SIZE, textureSize);
            FFT.SetTexture(KERNEL_SCALE, PROP_ID_BUFFER0, outputToInput ? input : buffer);
            FFT.Dispatch(KERNEL_SCALE, textureThreadGroups, textureThreadGroups, 1);
        }
    }

    void OnDestroy() {
        spectrumSettingsBuffer?.Release();
        inputPositionsBuffer?.Release();
        outputPositionsBuffer?.Release();
        outputSlopesBuffer?.Release();
        if (cascades != null)
            for (int i = 0; i < cascadesLengths.Length; i++) 
                cascades[i].Dispose();
    }
}
