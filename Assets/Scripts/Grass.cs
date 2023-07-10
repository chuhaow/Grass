using System.Collections;
using System.Collections.Generic;
using static System.Runtime.InteropServices.Marshal;
using UnityEngine;
using UnityEngine.TerrainUtils;
using UnityEngine.UIElements;

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
    [SerializeField] private bool updateGrass;
    [SerializeField] private float positionNoiseAmp;

    [Header("Wind Param")]
    [Range(0f, 1f)]
    [SerializeField] private float windFreq;
    [SerializeField] private WindNoiseMode windNoiseMode;

    private int defaultGroupCount = 128;
    private struct GrassData
    {
        private Vector4 position;
        private float saturation;
        private Vector2 worldUV;
        private float displacement;

        public Vector4 Position { get => position; set => position = value; }
        public float Saturation { get => saturation; set => saturation = value; }
        public Vector2 WorldUV { get => worldUV; set => worldUV = value; }
        public float Displacement { get => displacement; set => displacement = value; }
    }

    [SerializeField] private ComputeShader grassInit, windGenerator, cull;
    private ComputeBuffer grassDataBuffer, voteBuffer, scanBuffer, groupSumArrayBuffer, scannedGroupSumBuffer, culledGrassOutputBuffer, compactedGrassIndicesBuffer;
    private int numInstances, numGroups;

    private void OnEnable()
    {
        numInstances = fillSize * (int)grassDensity;
        numInstances *= numInstances;
        Debug.Log("NumInstances: " + numInstances.ToString());

        numGroups = numInstances / defaultGroupCount;
        if(numGroups > defaultGroupCount)
        {
            int powerOfTwo = defaultGroupCount;
            while(powerOfTwo < numGroups)
            {
                powerOfTwo *= 2;
            }
            numGroups = powerOfTwo;
        }
        else
        {
            while(defaultGroupCount % numGroups != 0)
            {
                numGroups++;
            }
        }
        Debug.Log("NumGroups: " + numGroups.ToString());
        grassDataBuffer = new ComputeBuffer(numInstances, SizeOf(typeof(GrassData)));
        voteBuffer = new ComputeBuffer(numInstances, 4);
        scanBuffer = new ComputeBuffer(numInstances, 4);
        groupSumArrayBuffer = new ComputeBuffer(numGroups, 4);
        scannedGroupSumBuffer = new ComputeBuffer(numGroups, 4);
       
        culledGrassOutputBuffer = new ComputeBuffer(numInstances, SizeOf(typeof(GrassData)));
 
        compactedGrassIndicesBuffer = new ComputeBuffer(numInstances, 4);

        windTexture = new RenderTexture(256, 256, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        windTexture.enableRandomWrite = true;
        windTexture.Create();
        UpdateGrass();
    }

    private void Start()
    {
        GenerateWind();
        
        //GrassData[] positions = new GrassData[fillSize * fillSize];
        //grassPosition.GetData(positions);
    }
    void Update()
    {
        
        CullGrass();
        GenerateWind();
        grassMaterial.SetBuffer("_GrassData", culledGrassOutputBuffer);
        grassMaterial.SetFloat("_Rotation", 0.0f);
        grassMaterial.SetTexture("_WindTex", windTexture);
        Graphics.DrawMeshInstancedProcedural(grassMesh, 0, grassMaterial, new Bounds(Vector3.zero, new Vector3(-400.0f, 200.0f, 400.0f)), grassDataBuffer.count);


        if (updateGrass)
        {
            UpdateGrass();
            updateGrass = false;
        }
        //Material grass2 = new Material(grassMaterial);
        //grass2.SetBuffer("_Position", grassPosition);
        //grass2.SetFloat("_Rotation", 45.0f);
        //Graphics.DrawMeshInstancedProcedural(grassMesh, 0, grass2, new Bounds(Vector3.zero, new Vector3(-500.0f, 200.0f, 500.0f)), grassPosition.count);

        //Material grass3 = new Material(grassMaterial);
        //grass3.SetBuffer("_Position", grassPosition);
        //grass3.SetFloat("_Rotation", 135.0f);
        //Graphics.DrawMeshInstancedProcedural(grassMesh, 0, grass3, new Bounds(Vector3.zero, new Vector3(-500.0f, 200.0f, 500.0f)), grassPosition.count);

    }

    private void UpdateGrass()
    {
        //if (autoAdjustFillsize) fillSize *= (int)grassDensity;
        
        Debug.Log(SizeOf(typeof(GrassData)));
        grassInit.SetInt("_FillSize", fillSize * (int)grassDensity);
        grassInit.SetFloat("_Density", grassDensity);
        grassInit.SetTexture(0, "_HeightMap", heightMap);
        grassInit.SetFloat("_DisplacementStrength", terrainMat.GetFloat("_DisplacementStrength"));
        grassInit.SetFloat("_Offset", heightOffset);
        grassInit.SetBuffer(0, "_Position", grassDataBuffer);
        grassInit.SetFloat("_PositionNoiseAmp", positionNoiseAmp);
        grassInit.Dispatch(0, Mathf.CeilToInt(fillSize * grassDensity / 8.0f), Mathf.CeilToInt(fillSize * grassDensity / 8.0f), 1);
        CullGrass();
        GenerateWind();
        //voteBuffer = new ComputeBuffer(fillSize * fillSize, sizeof(bool));
        //scanBuffer = new ComputeBuffer(fillSize * fillSize, sizeof(uint));
        //cull.SetBuffer(0, "_Vote", voteBuffer);
        //cull.SetMatrix("_ViewProjectionMatrix", VP);
        //cull.Dispatch(0, Mathf.CeilToInt((fillSize * fillSize) / 128.0f), 1, 1);

        grassMaterial.SetBuffer("_GrassData", grassDataBuffer);
        grassMaterial.SetTexture("_WindTex", windTexture);
    }

    private void GenerateWind()
    {
        windGenerator.SetTexture(0, "WindNoise", windTexture);
        windGenerator.SetFloat("_Time", Time.time);
        windGenerator.SetFloat("_Freq", windFreq);
        windGenerator.SetInt("_NoiseMode", (int)windNoiseMode);
        windGenerator.Dispatch(0, Mathf.CeilToInt(windTexture.width / 8.0f), Mathf.CeilToInt(windTexture.width / 8.0f), 1);

    }

    void CullGrass()
    {
        Matrix4x4 P = Camera.main.projectionMatrix;
        Matrix4x4 V = Camera.main.transform.worldToLocalMatrix;
        Matrix4x4 VP = P * V;

        int threadGroupSizeX = Mathf.CeilToInt(numInstances / 128.0f);
        //culledGrassOutputBuffer.SetData(empty);

        // Vote
        cull.SetMatrix("_ViewProjectionMatrix", VP);
        cull.SetBuffer(0, "_GrassDataBuffer", grassDataBuffer);
        cull.SetBuffer(0, "_VoteBuffer", voteBuffer);
        cull.Dispatch(0, Mathf.CeilToInt(numInstances / 128.0f), 1, 1);

        // Scan Instances
        cull.SetBuffer(1, "_VoteBuffer", voteBuffer);
        cull.SetBuffer(1, "_ScanBuffer", scanBuffer);
        cull.SetBuffer(1, "_ThreadGroupSumArray", groupSumArrayBuffer);
        cull.Dispatch(1, threadGroupSizeX, 1, 1);

        // Scan Groups
        cull.SetInt("_NumOfGroups", numGroups);
        cull.SetBuffer(2, "_ThreadGroupSumArrayIn", groupSumArrayBuffer);
        cull.SetBuffer(2, "_ThreadGroupSumArrayOut", scannedGroupSumBuffer);
        cull.Dispatch(2, Mathf.CeilToInt(numInstances / 1024), 1, 1);

        // Compact
        cull.SetBuffer(3, "_GrassDataBuffer", grassDataBuffer);
        cull.SetBuffer(3, "_VoteBuffer", voteBuffer);
        cull.SetBuffer(3, "_ScanBuffer", scanBuffer);
        cull.SetBuffer(3, "_CulledGrassOutputBuffer", culledGrassOutputBuffer);
        cull.SetBuffer(3, "_ThreadGroupSumArray", scannedGroupSumBuffer);
        cull.Dispatch(3, threadGroupSizeX, 1, 1);


    }
    void OnDisable()
    {
        grassDataBuffer.Release();
        voteBuffer.Release();
        scanBuffer.Release();
        groupSumArrayBuffer.Release();
        scannedGroupSumBuffer.Release();
        culledGrassOutputBuffer.Release();
        compactedGrassIndicesBuffer.Release();
        windTexture.Release();
    }
}
