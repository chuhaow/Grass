using System.Collections;
using System.Collections.Generic;
using static System.Runtime.InteropServices.Marshal;
using UnityEngine;
using UnityEngine.TerrainUtils;
using UnityEngine.UIElements;
using Unity.VisualScripting.FullSerializer;
using System.Drawing;

public class Grass : MonoBehaviour
{
    public enum WindNoiseMode
    {
        MARBLE,
        RIPPLE
    }
    
    [SerializeField] private Mesh grassMesh;
    [SerializeField] private Mesh lodGrassMesh;
    [SerializeField] private Material grassMaterial;
    [SerializeField] private Texture heightMap;
    [SerializeField] private Material terrainMat;
    [SerializeField] private float heightOffset;

    private RenderTexture windTexture;
    

    [Header("Grass Param")]
    [SerializeField] private int fillSize;
    [SerializeField] private int grassDensityPerChunk;
    [SerializeField] private int numChunks;
    [SerializeField] private bool updateGrass;
    [SerializeField] private float positionNoiseAmp;
    [SerializeField] private float lodDistance;

    [Header("Wind Param")]
    [Range(0f, 1f)]
    [SerializeField] private float windFreq;
    [SerializeField] private WindNoiseMode windNoiseMode;

    private const int DEFAULT_VOTE_THREAD_GROUP = 128;
    private const int DEFAULT_SCAN_THREAD_GROUP = 1024;
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


    private struct GrassChunk
    {
        private ComputeBuffer grassBuffer;
        private ComputeBuffer culledGrassBuffer;
        private Bounds bounds;
        private Material grassMat;

        public ComputeBuffer GrassBuffer { get => grassBuffer; set => grassBuffer = value; }
        public ComputeBuffer CulledGrassBuffer { get => culledGrassBuffer; set => culledGrassBuffer = value; }
        public Bounds Bounds { get => bounds; set => bounds = value; }
        public Material GrassMat { get => grassMat; set => grassMat = value; }
    }

    [SerializeField] private ComputeShader grassInit, windGenerator, cull;
    private ComputeBuffer grassDataBuffer, voteBuffer, scanBuffer, groupSumArrayBuffer, scannedGroupSumBuffer, culledGrassOutputBuffer, compactedGrassIndicesBuffer;
    private int instancesPerChunk, numGroups,numVoteGroups, numScanGroups, chunkDimension;
    private Bounds field;
    private GrassChunk[] chunks;

    void OnEnable()
    {
        instancesPerChunk  = Mathf.CeilToInt(fillSize / numChunks) * grassDensityPerChunk;;
        chunkDimension = instancesPerChunk ;
        instancesPerChunk  *= instancesPerChunk ;

        numGroups  = Mathf.CeilToInt(instancesPerChunk  / DEFAULT_VOTE_THREAD_GROUP);

        Debug.Log("instancesPerChunk : " + instancesPerChunk );
        Debug.Log("chunkDimension: " + chunkDimension);
        Debug.Log("numGroups : " + numGroups );
        if (numGroups  > DEFAULT_VOTE_THREAD_GROUP)
        {
            int powerOfTwo = DEFAULT_VOTE_THREAD_GROUP;
            while (powerOfTwo < numGroups )
                powerOfTwo *= 2;

            numGroups  = powerOfTwo;
        }
        else
        {
            while (DEFAULT_VOTE_THREAD_GROUP % numGroups  != 0)
                numGroups ++;
        }

        numVoteGroups  = Mathf.CeilToInt(instancesPerChunk / DEFAULT_VOTE_THREAD_GROUP);
        numScanGroups = Mathf.CeilToInt(instancesPerChunk / DEFAULT_SCAN_THREAD_GROUP);


        voteBuffer = new ComputeBuffer(instancesPerChunk , 4);
        scanBuffer = new ComputeBuffer(instancesPerChunk , 4);
        groupSumArrayBuffer = new ComputeBuffer(numGroups , 4);
        scannedGroupSumBuffer = new ComputeBuffer(numGroups , 4);

        grassInit.SetInt("_FillSize", fillSize);
        grassInit.SetInt("_ChunkDimension", chunkDimension);
        grassInit.SetInt("_NumChunks", numChunks);
        grassInit.SetInt("_Density", grassDensityPerChunk);
        grassInit.SetTexture(0, "_HeightMap", heightMap);
        grassInit.SetFloat("_DisplacementStrength", terrainMat.GetFloat("_DisplacementStrength"));
        grassInit.SetFloat("_Offset", heightOffset);
        grassInit.SetFloat("_PositionNoiseAmp", positionNoiseAmp);

        windTexture = new RenderTexture(256, 256, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        windTexture.enableRandomWrite = true;
        windTexture.Create();
        
        initializeChunks();

        field = new Bounds(Vector3.zero, new Vector3(-fillSize, terrainMat.GetFloat("_DisplacementStrength") * 2, fillSize));
    }

    void initializeChunks()
    {
        chunks = new GrassChunk[numChunks * numChunks];

        for (int x = 0; x < numChunks; ++x)
        {
            for (int y = 0; y < numChunks; ++y)
            {
                var chunk = CreateChunk(x, y);
                SetUpChunkInfo(chunk, x, y);
                chunks[x + y * numChunks] = chunk;
            }
        }
    }


    private GrassChunk CreateChunk(int OffsetX, int OffsetY)
    {
        GrassChunk chunk = new GrassChunk();

        chunk.GrassBuffer = new ComputeBuffer(instancesPerChunk, SizeOf(typeof(GrassData)));
        chunk.CulledGrassBuffer = new ComputeBuffer(instancesPerChunk, SizeOf(typeof(GrassData)));

        int singleChunkDim = Mathf.CeilToInt(fillSize / numChunks);
        Vector3 chunkCenter = new Vector3(0.0f, 0.0f, 0.0f);
        chunkCenter.y = 0.0f;
        chunkCenter.x = -(singleChunkDim * 0.5f * numChunks) + singleChunkDim * OffsetX;
        chunkCenter.z = -(singleChunkDim * 0.5f * numChunks) + singleChunkDim * OffsetY;
        chunkCenter.x += singleChunkDim * 0.5f;
        chunkCenter.z += singleChunkDim * 0.5f;

        chunk.Bounds = new Bounds(chunkCenter, new Vector3(-singleChunkDim, 10.0f, singleChunkDim));
        
        chunk.GrassMat = new Material(grassMaterial);

        return chunk;


    }

    private void SetUpChunkInfo(GrassChunk chunk,int OffsetX, int OffsetY)
    {
        grassInit.SetInt("OffsetX", OffsetX);
        grassInit.SetInt("OffsetY", OffsetY);
        grassInit.SetBuffer(0, "_Position", chunk.GrassBuffer);
        grassInit.Dispatch(0, Mathf.CeilToInt(fillSize / numChunks) * (int)grassDensityPerChunk, Mathf.CeilToInt(fillSize / numChunks) * (int)grassDensityPerChunk, 1);

        chunk.GrassMat.SetBuffer("_GrassData", chunk.CulledGrassBuffer);
        chunk.GrassMat.SetTexture("_WindTex", windTexture);
        chunk.GrassMat.SetInt("_ChunkNum", OffsetX + OffsetY * numChunks);
    }

    void Update()
    {
        Matrix4x4 P = Camera.main.projectionMatrix;
        Matrix4x4 V = Camera.main.transform.worldToLocalMatrix;
        Matrix4x4 VP = P * V;
        GenerateWind();
        for (int i = 0; i < numChunks * numChunks; ++i)
        {
            float dist = Vector3.Distance(Camera.main.transform.position, chunks[i].Bounds.center);
            //if (shouldCullChunk(chunks[i])) continue;
            CullGrass(chunks[i], VP);
            //Graphics.DrawMeshInstancedProcedural(grassMesh, 0, chunks[i].GrassMat, field, chunks[i].GrassBuffer.count);
            if (dist < lodDistance)
            {
                Graphics.DrawMeshInstancedProcedural(grassMesh, 0, chunks[i].GrassMat, field, chunks[i].GrassBuffer.count);
            }
            else
            {
                Graphics.DrawMeshInstancedProcedural(lodGrassMesh, 0, chunks[i].GrassMat, field, chunks[i].GrassBuffer.count);
            }
        }


    }

    private bool shouldCullChunk(GrassChunk chunk)
    {
        
        Vector3 closest = chunk.Bounds.ClosestPoint(Camera.main.transform.position);
        var heading = closest - Camera.main.transform.position;
        var dot = Vector3.Dot(heading, Camera.main.transform.forward);
        return dot < -0.9f;
    }

    private void GenerateWind()
    {
        windGenerator.SetTexture(0, "WindNoise", windTexture);
        windGenerator.SetFloat("_Time", Time.time);
        windGenerator.SetFloat("_Freq", windFreq);
        windGenerator.SetInt("_NoiseMode", 0);
        windGenerator.Dispatch(0, Mathf.CeilToInt(windTexture.height / 8.0f), Mathf.CeilToInt(windTexture.height / 8.0f), 1);

    }

    void CullGrass(GrassChunk chunk, Matrix4x4 VP)
    {

        // Vote
        cull.SetMatrix("_ViewProjectionMatrix", VP);
        cull.SetBuffer(0, "_GrassDataBuffer", chunk.GrassBuffer);
        cull.SetBuffer(0, "_VoteBuffer", voteBuffer);
        cull.Dispatch(0, numVoteGroups, 1, 1);

        // Scan Instances
        cull.SetBuffer(1, "_VoteBuffer", voteBuffer);
        cull.SetBuffer(1, "_ScanBuffer", scanBuffer);
        cull.SetBuffer(1, "_ThreadGroupSumArray", groupSumArrayBuffer);
        cull.Dispatch(1, numGroups, 1, 1);

        // Scan Groups
        cull.SetInt("_NumOfGroups", numGroups);
        cull.SetBuffer(2, "_ThreadGroupSumArrayIn", groupSumArrayBuffer);
        cull.SetBuffer(2, "_ThreadGroupSumArrayOut", scannedGroupSumBuffer);
        cull.Dispatch(2, numScanGroups, 1, 1);

        // Compact
        cull.SetBuffer(3, "_GrassDataBuffer", chunk.GrassBuffer);
        cull.SetBuffer(3, "_VoteBuffer", voteBuffer);
        cull.SetBuffer(3, "_ScanBuffer", scanBuffer);
        cull.SetBuffer(3, "_CulledGrassOutputBuffer", chunk.CulledGrassBuffer);
        cull.SetBuffer(3, "_ThreadGroupSumArray", scannedGroupSumBuffer);
        cull.Dispatch(3, numGroups, 1, 1);


    }
    void OnDisable()
    {
        windTexture.Release();
        //grassDataBuffer.Release();
        voteBuffer.Release();
        scanBuffer.Release();
        groupSumArrayBuffer.Release();
        scannedGroupSumBuffer.Release();
        //culledGrassOutputBuffer.Release();
        //compactedGrassIndicesBuffer.Release();
        ReleaseChunks();
    }

    private void ReleaseChunks()
    {
        foreach(var chunk in chunks) { 
            chunk.CulledGrassBuffer.Release();
            chunk.GrassBuffer.Release();
            
        }
    }

    void OnDrawGizmos()
    {
        Gizmos.color = UnityEngine.Color.yellow;
        if (chunks != null)
        {
            for (int i = 0; i < numChunks * numChunks; ++i)
            {
                Gizmos.DrawWireCube(chunks[i].Bounds.center, chunks[i].Bounds.size);
            }
        }
    }
}
