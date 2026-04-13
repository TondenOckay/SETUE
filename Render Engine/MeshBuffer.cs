using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Silk.NET.Vulkan;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace SETUE.RenderEngine;

public unsafe static class MeshBuffer
{
    private static Vk _vk = null!;
    private static Device _device;
    private static PhysicalDevice _physDevice;

    // Store full mesh data
    public struct MeshData
    {
        public VkBuffer VertexBuffer;
        public DeviceMemory VertexMemory;
        public VkBuffer IndexBuffer;
        public DeviceMemory IndexMemory;
        public uint VertexCount;
        public uint IndexCount;
        public uint VertexStride; // in bytes
    }

    private static readonly Dictionary<string, MeshData> _meshes = new();

    public static void Init()
    {
        _vk = Vulkan.VK;
        _device = Vulkan.Device;
        _physDevice = Vulkan.PhysicalDevice;
        LoadMeshes();
    }

    private static void LoadMeshes()
    {
        string path = "Render Engine/MeshBuffer.csv";
        if (!File.Exists(path))
        {
            Console.WriteLine("[MeshBuffer] Missing MeshBuffer.csv; no meshes loaded.");
            return;
        }

        var lines = File.ReadAllLines(path).Skip(1).Where(l => !string.IsNullOrWhiteSpace(l) && !l.Trim().StartsWith('#'));
        var meshData = new Dictionary<string, (List<float> verts, List<uint> indices)>();

        foreach (var line in lines)
        {
            var p = line.Split(',');
            if (p.Length < 3) continue;
            string type   = p[0].Trim();
            string meshId = p[1].Trim();

            if (!meshData.ContainsKey(meshId))
                meshData[meshId] = (new List<float>(), new List<uint>());

            var (verts, indices) = meshData[meshId];

            switch (type)
            {
                case "primitive":
                    float a = ParseFloat(p[2]); float b = ParseFloat(p[3]); float c = ParseFloat(p[4]);
                    float d = ParseFloat(p[5]); float e = ParseFloat(p[6]); float f = ParseFloat(p[7]);
                    GeneratePrimitive(meshId, a, b, c, d, e, f, verts, indices);
                    break;
                case "vertex":
                    for (int i = 2; i < p.Length && i < 10; i++)
                        verts.Add(ParseFloat(p[i]));
                    break;
                case "index":
                    for (int i = 2; i < p.Length; i++)
                        if (!string.IsNullOrWhiteSpace(p[i]))
                            indices.Add(uint.Parse(p[i].Trim()));
                    break;
            }
        }

        foreach (var kv in meshData)
        {
            string id = kv.Key;
            var (verts, indices) = kv.Value;
            if (verts.Count == 0 || indices.Count == 0) continue;
            Upload(id, verts.ToArray(), indices.ToArray());
            Console.WriteLine($"[MeshBuffer] Loaded '{id}' with {verts.Count/8} vertices, {indices.Count} indices");
        }
    }

    private static float ParseFloat(string s) => float.TryParse(s.Trim(), out var f) ? f : 0f;

    private static void GeneratePrimitive(string shape, float a, float b, float c, float d, float e, float f,
                                          List<float> verts, List<uint> indices)
    {
        uint baseIndex = (uint)(verts.Count / 8);
        switch (shape.ToLower())
        {
            case "cube": GenCube(a, verts, indices, baseIndex); break;
            case "sphere": GenSphere(a, (int)b, (int)c, verts, indices, baseIndex); break;
            case "quad": GenQuad(a, b, verts, indices, baseIndex); break;
            case "grid": GenGrid((int)a, (int)b, d, e, f, verts, indices, baseIndex); break;
            case "axis": GenAxis(a, verts, indices, baseIndex); break;
            default: Console.WriteLine($"[MeshBuffer] Unknown primitive '{shape}'"); break;
        }
    }

    private static void GenCube(float size, List<float> v, List<uint> idx, uint baseIdx)
    {
        float s = size * 0.5f;
        float[] verts = {
            -s,-s, s, 0,0,1, 0,0,  s,-s, s, 0,0,1, 1,0,  s, s, s, 0,0,1, 1,1, -s, s, s, 0,0,1, 0,1,
             s,-s,-s, 0,0,-1,0,0, -s,-s,-s, 0,0,-1,1,0, -s, s,-s, 0,0,-1,1,1,  s, s,-s, 0,0,-1,0,1,
            -s,-s,-s,-1,0,0,0,0, -s,-s, s,-1,0,0,1,0, -s, s, s,-1,0,0,1,1, -s, s,-s,-1,0,0,0,1,
             s,-s, s, 1,0,0,0,0,  s,-s,-s, 1,0,0,1,0,  s, s,-s, 1,0,0,1,1,  s, s, s, 1,0,0,0,1,
            -s, s, s, 0,1,0,0,0,  s, s, s, 0,1,0,1,0,  s, s,-s, 0,1,0,1,1, -s, s,-s, 0,1,0,0,1,
            -s,-s,-s, 0,-1,0,0,0, s,-s,-s, 0,-1,0,1,0, s,-s, s, 0,-1,0,1,1, -s,-s, s, 0,-1,0,0,1
        };
        v.AddRange(verts);
        for (uint f = 0; f < 6; f++)
        {
            uint b = baseIdx + f * 4;
            idx.Add(b); idx.Add(b+1); idx.Add(b+2);
            idx.Add(b); idx.Add(b+2); idx.Add(b+3);
        }
    }

    private static void GenSphere(float r, int slices, int stacks, List<float> v, List<uint> idx, uint baseIdx)
    {
        for (int st = 0; st <= stacks; st++)
        {
            float phi = MathF.PI * st / stacks;
            for (int sl = 0; sl <= slices; sl++)
            {
                float theta = 2 * MathF.PI * sl / slices;
                float x = MathF.Sin(phi) * MathF.Cos(theta);
                float y = MathF.Cos(phi);
                float z = MathF.Sin(phi) * MathF.Sin(theta);
                float u = (float)sl / slices;
                float v_ = (float)st / stacks;
                v.AddRange(new[] { x*r, y*r, z*r, x, y, z, u, v_ });
            }
        }
        for (int st = 0; st < stacks; st++)
            for (int sl = 0; sl < slices; sl++)
            {
                uint a = baseIdx + (uint)(st * (slices+1) + sl);
                uint b = a + (uint)(slices+1);
                idx.Add(a); idx.Add(b); idx.Add(a+1);
                idx.Add(b); idx.Add(b+1); idx.Add(a+1);
            }
    }

    private static void GenQuad(float w, float h, List<float> v, List<uint> idx, uint baseIdx)
    {
        float hw = w*0.5f, hh = h*0.5f;
        v.AddRange(new[] { -hw,-hh,0f, 0,0,1, 0,0,  hw,-hh,0f, 0,0,1, 1,0,  hw,hh,0f, 0,0,1, 1,1, -hw,hh,0f, 0,0,1, 0,1 });
        idx.Add(baseIdx); idx.Add(baseIdx+1); idx.Add(baseIdx+2);
        idx.Add(baseIdx); idx.Add(baseIdx+2); idx.Add(baseIdx+3);
    }

    private static void GenGrid(int halfExt, int spacing, float r, float g, float b, List<float> v, List<uint> idx, uint baseIdx)
    {
        for (int x = -halfExt; x <= halfExt; x += spacing)
        {
            v.AddRange(new[] { (float)x, 0f,  halfExt, r, g, b, 0f, 0f });
            v.AddRange(new[] { (float)x, 0f, -halfExt, r, g, b, 0f, 0f });
            idx.Add(baseIdx); idx.Add(baseIdx+1); baseIdx += 2;
        }
        for (int z = -halfExt; z <= halfExt; z += spacing)
        {
            v.AddRange(new[] {  halfExt, 0f, (float)z, r, g, b, 0f, 0f });
            v.AddRange(new[] { -halfExt, 0f, (float)z, r, g, b, 0f, 0f });
            idx.Add(baseIdx); idx.Add(baseIdx+1); baseIdx += 2;
        }
    }

    private static void GenAxis(float len, List<float> v, List<uint> idx, uint baseIdx)
    {
        v.AddRange(new[] { -len,0f,0f, 1,0,0, 0,0,  len,0f,0f, 1,0,0, 0,0 });
        v.AddRange(new[] { 0f,0f,-len, 0,0,1, 0,0,  0f,0f,len, 0,0,1, 0,0 });
        idx.Add(baseIdx); idx.Add(baseIdx+1);
        idx.Add(baseIdx+2); idx.Add(baseIdx+3);
    }

    private static void Upload(string id, float[] vertices, uint[] indices)
    {
        uint vertexCount = (uint)(vertices.Length / 8); // 8 floats per vertex (pos3, norm3, uv2)
        uint vertexStride = 8 * sizeof(float); // 32 bytes

        ulong vSize = (ulong)(vertices.Length * sizeof(float));
        ulong iSize = (ulong)(indices.Length * sizeof(uint));

        Vulkan.CreateBuffer(vSize, BufferUsageFlags.VertexBufferBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            out VkBuffer vbuf, out DeviceMemory vmem);
        Vulkan.CreateBuffer(iSize, BufferUsageFlags.IndexBufferBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            out VkBuffer ibuf, out DeviceMemory imem);

        void* mapped;
        _vk.MapMemory(_device, vmem, 0, vSize, 0, &mapped);
        fixed (float* src = vertices) System.Buffer.MemoryCopy(src, mapped, (long)vSize, (long)vSize);
        _vk.UnmapMemory(_device, vmem);

        _vk.MapMemory(_device, imem, 0, iSize, 0, &mapped);
        fixed (uint* src = indices) System.Buffer.MemoryCopy(src, mapped, (long)iSize, (long)iSize);
        _vk.UnmapMemory(_device, imem);

        _meshes[id] = new MeshData
        {
            VertexBuffer = vbuf,
            VertexMemory = vmem,
            IndexBuffer = ibuf,
            IndexMemory = imem,
            VertexCount = vertexCount,
            IndexCount = (uint)indices.Length,
            VertexStride = vertexStride
        };
    }

    // New method returning full MeshData
    public static bool GetMeshData(string shape, out MeshData data)
    {
        string key = shape.ToLower();
        if (_meshes.TryGetValue(key, out data))
            return true;
        Console.WriteLine($"[MeshBuffer] Mesh '{key}' not found, falling back to 'cube'");
        if (_meshes.TryGetValue("cube", out data))
            return true;
        data = default;
        return false;
    }

    // Legacy Get for compatibility (if any code still uses it)
    public static bool Get(string shape, out VkBuffer vbuf, out VkBuffer ibuf, out uint indexCount)
    {
        if (GetMeshData(shape, out var data))
        {
            vbuf = data.VertexBuffer;
            ibuf = data.IndexBuffer;
            indexCount = data.IndexCount;
            return true;
        }
        vbuf = default; ibuf = default; indexCount = 0;
        return false;
    }

    public static void Destroy()
    {
        foreach (var m in _meshes.Values)
        {
            _vk.DestroyBuffer(_device, m.VertexBuffer, null);
            _vk.FreeMemory(_device, m.VertexMemory, null);
            _vk.DestroyBuffer(_device, m.IndexBuffer, null);
            _vk.FreeMemory(_device, m.IndexMemory, null);
        }
    }
}
