using System;
using UnityEngine;
using System.Runtime.InteropServices;

public class WavesCascade
{
    public RenderTexture Displacement => displacement;
    public RenderTexture Derivatives => derivatives;
    public RenderTexture Turbulence => turbulence;

    public RenderTexture GaussianNoise => gaussianNoise;
    public RenderTexture PrecomputedData => precomputedData;
    public RenderTexture InitialSpectrum => initialSpectrum;
    public RenderTexture Buffer => buffer;

    readonly int size;
    readonly ComputeShader initialSpectrumShader;
    readonly ComputeShader timeDependentSpectrumShader;
    readonly ComputeShader texturesMergerShader;
    readonly RenderTexture gaussianNoise;
    readonly ComputeBuffer paramsBuffer;
    readonly RenderTexture initialSpectrum;
    readonly RenderTexture precomputedData;
    
    readonly RenderTexture buffer;
    readonly RenderTexture DxDz;
    readonly RenderTexture DyDxz;
    readonly RenderTexture DyxDyz;
    readonly RenderTexture DxxDzz;

    readonly RenderTexture displacement;
    readonly RenderTexture derivatives;
    readonly RenderTexture turbulence;

    readonly OceanShadersHandler.WaveSettings waves;

    readonly OceanShadersHandler handler;

    int KERNEL_INITIAL_SPECTRUM, KERNEL_CONJUGATE_SPECTRUM,KERNEL_TIME_DEPENDENT_SPECTRUMS, KERNEL_RESULT_TEXTURES;

    public WavesCascade(OceanShadersHandler handler)
    {
        this.handler = handler;

        this.size = handler.textureSize;
        this.initialSpectrumShader = handler.InitialSpectrum;
        this.timeDependentSpectrumShader = handler.TimeDependentSpectrum;
        this.texturesMergerShader = handler.WavesTexturesMerger;
        this.gaussianNoise = handler.NoiseTexture;
        this.waves = handler.waveSettings;

        KERNEL_INITIAL_SPECTRUM = initialSpectrumShader.FindKernel("CalculateInitialSpectrum");
        KERNEL_CONJUGATE_SPECTRUM = initialSpectrumShader.FindKernel("CalculateConjugatedSpectrum");
        KERNEL_TIME_DEPENDENT_SPECTRUMS = timeDependentSpectrumShader.FindKernel("CalculateAmplitudes");
        KERNEL_RESULT_TEXTURES = texturesMergerShader.FindKernel("FillResultTextures");

        initialSpectrum = OceanShadersHandler.CreateRenderTexture(size, size, RenderTextureFormat.ARGBFloat);
        precomputedData = OceanShadersHandler.CreateRenderTexture(size, size, RenderTextureFormat.ARGBFloat);
        displacement = OceanShadersHandler.CreateRenderTexture(size, size, RenderTextureFormat.ARGBFloat);
        derivatives = OceanShadersHandler.CreateRenderTexture(size, size, RenderTextureFormat.ARGBFloat, true, FilterMode.Trilinear, 6, true);
        turbulence = OceanShadersHandler.CreateRenderTexture(size, size, RenderTextureFormat.ARGBFloat, true, FilterMode.Trilinear, 6, true);
        paramsBuffer = new ComputeBuffer(2, Marshal.SizeOf<OceanShadersHandler.SpectrumSettings>());

        buffer = OceanShadersHandler.CreateRenderTexture(size, size);
        DxDz = OceanShadersHandler.CreateRenderTexture(size, size);
        DyDxz = OceanShadersHandler.CreateRenderTexture(size, size);
        DyxDyz = OceanShadersHandler.CreateRenderTexture(size, size);
        DxxDzz = OceanShadersHandler.CreateRenderTexture(size, size);
    }

    public void Dispose()
    {
        paramsBuffer?.Release();
    }

    public void CalculateInitials(float lengthScale, float cutoffLow, float cutoffHigh)
    {
        initialSpectrumShader.SetInt("Size", size);
        initialSpectrumShader.SetFloat("LengthScale", lengthScale);
        initialSpectrumShader.SetFloat("CutoffHigh", cutoffHigh);
        initialSpectrumShader.SetFloat("CutoffLow", cutoffLow);

        initialSpectrumShader.SetFloat("GravityAcceleration", waves.g);
        initialSpectrumShader.SetFloat("Depth", waves.depth);

        paramsBuffer.SetData(handler.spectrums);
        initialSpectrumShader.SetBuffer(KERNEL_INITIAL_SPECTRUM, "Spectrums", paramsBuffer);

        initialSpectrumShader.SetTexture(KERNEL_INITIAL_SPECTRUM, "H0K", buffer);
        initialSpectrumShader.SetTexture(KERNEL_INITIAL_SPECTRUM, "WavesData", precomputedData);
        initialSpectrumShader.SetTexture(KERNEL_INITIAL_SPECTRUM, "Noise", gaussianNoise);
        initialSpectrumShader.Dispatch(KERNEL_INITIAL_SPECTRUM, handler.textureThreadGroups, handler.textureThreadGroups, 1);

        initialSpectrumShader.SetTexture(KERNEL_CONJUGATE_SPECTRUM, "H0", initialSpectrum);
        initialSpectrumShader.SetTexture(KERNEL_CONJUGATE_SPECTRUM, "H0K", buffer);
        initialSpectrumShader.Dispatch(KERNEL_CONJUGATE_SPECTRUM, handler.textureThreadGroups, handler.textureThreadGroups, 1);
    }

    public void CalculateWavesAtTime(float time)
    {
        // Calculating complex amplitudes
        timeDependentSpectrumShader.SetTexture(KERNEL_TIME_DEPENDENT_SPECTRUMS, "Dx_Dz", DxDz);
        timeDependentSpectrumShader.SetTexture(KERNEL_TIME_DEPENDENT_SPECTRUMS, "Dy_Dxz", DyDxz);
        timeDependentSpectrumShader.SetTexture(KERNEL_TIME_DEPENDENT_SPECTRUMS, "Dyx_Dyz", DyxDyz);
        timeDependentSpectrumShader.SetTexture(KERNEL_TIME_DEPENDENT_SPECTRUMS, "Dxx_Dzz", DxxDzz);
        timeDependentSpectrumShader.SetTexture(KERNEL_TIME_DEPENDENT_SPECTRUMS, "H0", initialSpectrum);
        timeDependentSpectrumShader.SetTexture(KERNEL_TIME_DEPENDENT_SPECTRUMS, "WavesData", precomputedData);
        timeDependentSpectrumShader.SetFloat("Time", time);
        timeDependentSpectrumShader.Dispatch(KERNEL_TIME_DEPENDENT_SPECTRUMS, handler.textureThreadGroups, handler.textureThreadGroups, 1);
        
        // Calculating IFFTs of complex amplitudes
        handler.IFFT2D(DxDz, buffer);
        handler.IFFT2D(DyDxz, buffer);
        handler.IFFT2D(DyxDyz, buffer);
        handler.IFFT2D(DxxDzz, buffer);

        
        // Filling displacement and normals textures
        texturesMergerShader.SetFloat("DeltaTime", Time.fixedDeltaTime);

        texturesMergerShader.SetTexture(KERNEL_RESULT_TEXTURES, "Dx_Dz", DxDz);
        texturesMergerShader.SetTexture(KERNEL_RESULT_TEXTURES, "Dy_Dxz", DyDxz);
        texturesMergerShader.SetTexture(KERNEL_RESULT_TEXTURES, "Dyx_Dyz", DyxDyz);
        texturesMergerShader.SetTexture(KERNEL_RESULT_TEXTURES, "Dxx_Dzz", DxxDzz);
        texturesMergerShader.SetTexture(KERNEL_RESULT_TEXTURES, "Displacement", displacement);
        texturesMergerShader.SetTexture(KERNEL_RESULT_TEXTURES, "Derivatives", derivatives);
        texturesMergerShader.SetTexture(KERNEL_RESULT_TEXTURES, "Turbulence", turbulence);
        texturesMergerShader.SetFloat("Lambda", waves.lambda);
        texturesMergerShader.Dispatch(KERNEL_RESULT_TEXTURES, handler.textureThreadGroups, handler.textureThreadGroups, 1);

        /*derivatives.GenerateMips();
        turbulence.GenerateMips();*/
    }
}
