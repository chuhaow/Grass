using System.Collections;
using System.Collections.Generic;
using static System.Runtime.InteropServices.Marshal;
using UnityEngine;
using UnityEngine.TerrainUtils;

public class Grass : MonoBehaviour
{
    public enum WindNoiseMode
    {
        MARBLE,
        RIPPLE
    }
    
    [SerializeField] private Mesh grassMesh;
    [SerializeField] private Material grassMaterial;
    [SerializeField] private Texture heightMap;
    [SerializeField] private Material terrainMat;
    [SerializeField] private float heightOffset;

    private RenderTexture windTexture;
    

    [Header("Grass Param")]
    [SerializeField] private int fillSize;
    [SerializeField] private float grassDensity;
    [Tooltip("Automatically adjust the fill size based on density")]
    [SerializeField] private bool autoAdjustFillsize;

    [Header("Wind Param")]
    [Range(0f, 1f)]
    [SerializeField] private float windFreq;
    [SerializeField] private WindNoiseMode windNoiseMode;

    private struct GrassData
    {
        private Vector4 position;
        private float saturation;
        private Vector2 worldUV;

        public Vector4 Position { get => position; set => position = value; }
        public float Saturation { get => saturation; set => saturation = value; }
        public Vector2 WorldUV { get => worldUV; set => worldUV = value; }
    }

    [SerializeField] private ComputeShader grassInit, windGenerator;
    private ComputeBuffer grassPosition;

    private void OnEnable()
    {
        windTexture = new RenderTexture(256, 256, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        windTexture.enableRandomWrite = true;
        windTexture.Create();
    }

    private void Start()
    {
        GenerateWind();
        SetUpGrass();
        //GrassData[] positions = new GrassData[fillSize * fillSize];
        //grassPosition.GetData(positions);
    }
    void Update()
    {
        GenerateWind();
        grassMaterial.SetBuffer("_GrassData", grassPosition);
        grassMaterial.SetFloat("_Rotation", 0.0f);
        Graphics.DrawMeshInstancedProcedural(grassMesh, 0, grassMaterial, new Bounds(Vector3.zero, new Vector3(-400.0f, 200.0f, 400.0f)), grassPosition.count);

        //Material grass2 = new Material(grassMaterial);
        //grass2.SetBuffer("_Position", grassPosition);
        //grass2.SetFloat("_Rotation", 45.0f);
        //Graphics.DrawMeshInstancedProcedural(grassMesh, 0, grass2, new Bounds(Vector3.zero, new Vector3(-500.0f, 200.0f, 500.0f)), grassPosition.count);

        //Material grass3 = new Material(grassMaterial);
        //grass3.SetBuffer("_Position", grassPosition);
        //grass3.SetFloat("_Rotation", 135.0f);
        //Graphics.DrawMeshInstancedProcedural(grassMesh, 0, grass3, new Bounds(Vector3.zero, new Vector3(-500.0f, 200.0f, 500.0f)), grassPosition.count);

    }

    private void SetUpGrass()
    {
        if (autoAdjustFillsize) fillSize *= (int)grassDensity;
        grassPosition = new ComputeBuffer(fillSize * fillSize, SizeOf(typeof(GrassData)));
        Debug.Log(SizeOf(typeof(GrassData)));
        grassInit.SetInt("_FillSize", fillSize);
        grassInit.SetFloat("_Density", grassDensity);
        grassInit.SetTexture(0, "_HeightMap", heightMap);
        grassInit.SetFloat("_DisplacementStrength", terrainMat.GetFloat("_DisplacementStrength"));
        grassInit.SetFloat("_Offset", heightOffset);
        grassInit.SetBuffer(0, "_Position", grassPosition);
        grassInit.Dispatch(0, Mathf.CeilToInt(fillSize / 8.0f), Mathf.CeilToInt(fillSize / 8.0f), 1);


        grassMaterial.SetBuffer("_GrassData", grassPosition);
        grassMaterial.SetTexture("_WindTex", windTexture);
    }

    private void GenerateWind()
    {
        windGenerator.SetTexture(0, "WindNoise", windTexture);
        windGenerator.SetFloat("_Time", Time.time);
        windGenerator.SetFloat("_Freq", windFreq);
        windGenerator.SetInt("_NoiseMode", (int)windNoiseMode);
        windGenerator.Dispatch(0, Mathf.CeilToInt(fillSize / 8.0f), Mathf.CeilToInt(fillSize / 8.0f), 1);

    }
    void OnDisable()
    {
        grassPosition.Release();
    }
}
