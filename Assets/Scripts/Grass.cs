using System.Collections;
using System.Collections.Generic;
using static System.Runtime.InteropServices.Marshal;
using UnityEngine;
using UnityEngine.TerrainUtils;

public class Grass : MonoBehaviour
{
    
    [SerializeField] private Mesh grassMesh;
    [SerializeField] private Material grassMaterial;
    [SerializeField] private Texture heightMap;
    [SerializeField] private Material terrainMat;
    [SerializeField] private float heightOffset;
   

    [Header("Grass Param")]
    [SerializeField] private int fillSize;
    [SerializeField] private float grassDensity;


    private struct GrassData
    {
        private Vector4 position;
        //private Vector2 uv;

        public Vector4 Position { get => position; set => position = value; }
       // public Vector2 UV { get => uv; set => uv = value; }
    }

    [SerializeField] private ComputeShader grassInit;
    private ComputeBuffer grassPosition;

    private void Start()
    {
        grassPosition = new ComputeBuffer(fillSize * fillSize, SizeOf(typeof(GrassData)));
        Debug.Log(SizeOf(typeof(GrassData)));
        grassInit.SetInt("_FillSize", fillSize);
        grassInit.SetFloat("_Density", grassDensity);
        grassInit.SetTexture(0, "_HeightMap", heightMap);
        grassInit.SetFloat("_DisplacementStrength", terrainMat.GetFloat("_DisplacementStrength"));
        grassInit.SetFloat("_Offset", heightOffset);
        grassInit.SetBuffer(0,"_Position", grassPosition);
        grassInit.Dispatch(0, Mathf.CeilToInt(fillSize / 8.0f), Mathf.CeilToInt(fillSize / 8.0f), 1);
        grassMaterial.SetBuffer("_Position", grassPosition);

        //GrassData[] positions = new GrassData[fillSize * fillSize];
        //grassPosition.GetData(positions);
    }
    void Update()
    {
        grassMaterial.SetBuffer("_Position", grassPosition);
        grassMaterial.SetFloat("_Rotation", 0.0f);
        Graphics.DrawMeshInstancedProcedural(grassMesh, 0, grassMaterial, new Bounds(Vector3.zero, new Vector3(-500.0f, 200.0f, 500.0f)), grassPosition.count);

        Material grass2 = new Material(grassMaterial);
        grass2.SetBuffer("_Position", grassPosition);
        grass2.SetFloat("_Rotation", 45.0f);
        Graphics.DrawMeshInstancedProcedural(grassMesh, 0, grass2, new Bounds(Vector3.zero, new Vector3(-500.0f, 200.0f, 500.0f)), grassPosition.count);

        Material grass3 = new Material(grassMaterial);
        grass3.SetBuffer("_Position", grassPosition);
        grass3.SetFloat("_Rotation", 135.0f);
        Graphics.DrawMeshInstancedProcedural(grassMesh, 0, grass3, new Bounds(Vector3.zero, new Vector3(-500.0f, 200.0f, 500.0f)), grassPosition.count);

    }
    void OnDisable()
    {
        grassPosition.Release();
    }
}
