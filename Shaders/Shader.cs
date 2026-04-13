using System;
using System.IO;
using System.Collections.Generic;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace SETUE.RenderEngine
{
    public class Shader
    {
        public string Id               { get; set; } = "";
        public string VertPath         { get; set; } = "";
        public string FragPath         { get; set; } = "";
        public string Topology         { get; set; } = "triangle_list";
        public string CullMode         { get; set; } = "none";
        public bool   DepthTest        { get; set; } = false;
        public bool   DepthWrite       { get; set; } = false;
        public string DepthCompare     { get; set; } = "less";
        public bool   StencilTest      { get; set; } = false;
        public bool   DepthBounds      { get; set; } = false;
        public uint   PushConstantSize { get; set; } = 144;
        public string Type             { get; set; } = "2d";
        public bool   HasSampler       { get; set; } = false;
        public bool   BlendEnable      { get; set; } = false;
        public string SrcColorBlend    { get; set; } = "one";
        public string DstColorBlend    { get; set; } = "zero";
        public string ColorBlendOp     { get; set; } = "add";
        public string SrcAlphaBlend    { get; set; } = "one";
        public string DstAlphaBlend    { get; set; } = "zero";
        public string AlphaBlendOp     { get; set; } = "add";
        public DescriptorSetLayout SamplerLayout;

        public Silk.NET.Vulkan.Pipeline Handle;
        public PipelineLayout           Layout;
    }

    public unsafe static class Shaders
    {
        private static Vk     _vk     = null!;
        private static Device _device;
        private static string _csvPath = "Shaders/Shader.csv";

        private static Dictionary<string, Shader> _pipelines = new();
        private static Dictionary<string, Silk.NET.Vulkan.Pipeline> _pipelineHandles = new();
        private static Dictionary<string, PipelineLayout> _pipelineLayouts = new();

        public static Silk.NET.Vulkan.Pipeline GetHandle(string id) =>
            _pipelineHandles.TryGetValue(id, out var h) ? h : default;

        public static PipelineLayout GetLayout(string id) =>
            _pipelineLayouts.TryGetValue(id, out var l) ? l : default;

        public static DescriptorSetLayout GetSamplerLayout(string id) =>
            _pipelines.TryGetValue(id, out var e) ? e.SamplerLayout : default;

        public static IReadOnlyDictionary<string, Shader> All => _pipelines;

        public static void Load()
        {
            if (!File.Exists(_csvPath)) { Console.WriteLine($"[Shaders] Missing {_csvPath}"); return; }

            _pipelines.Clear();
            _pipelineHandles.Clear();
            _pipelineLayouts.Clear();

            var lines   = File.ReadAllLines(_csvPath);
            var headers = lines[0].Split(',');

            int idxId    = Array.IndexOf(headers, "id");
            int idxVert  = Array.IndexOf(headers, "vert_shader");
            int idxFrag  = Array.IndexOf(headers, "frag_shader");
            int idxTopo  = Array.IndexOf(headers, "topology");
            int idxCull  = Array.IndexOf(headers, "cull_mode");
            int idxDepT  = Array.IndexOf(headers, "depth_test");
            int idxDepW  = Array.IndexOf(headers, "depth_write");
            int idxDepC  = Array.IndexOf(headers, "depth_compare");
            int idxSten  = Array.IndexOf(headers, "stencil_test");
            int idxBnds  = Array.IndexOf(headers, "depth_bounds");
            int idxPush  = Array.IndexOf(headers, "push_constant_size");
            int idxType  = Array.IndexOf(headers, "type");
            int idxSamp  = Array.IndexOf(headers, "has_sampler");
            int idxBlend = Array.IndexOf(headers, "blend_enable");
            int idxSrcC  = Array.IndexOf(headers, "src_color_blend");
            int idxDstC  = Array.IndexOf(headers, "dst_color_blend");
            int idxColOp = Array.IndexOf(headers, "color_blend_op");
            int idxSrcA  = Array.IndexOf(headers, "src_alpha_blend");
            int idxDstA  = Array.IndexOf(headers, "dst_alpha_blend");
            int idxAlphaOp = Array.IndexOf(headers, "alpha_blend_op");

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
                var p = line.Split(',');

                var entry = new Shader
                {
                    Id               = idxId   >= 0 ? p[idxId].Trim()                      : "",
                    VertPath         = idxVert  >= 0 ? p[idxVert].Trim()                    : "",
                    FragPath         = idxFrag  >= 0 ? p[idxFrag].Trim()                    : "",
                    Topology         = idxTopo  >= 0 ? p[idxTopo].Trim()                    : "triangle_list",
                    CullMode         = idxCull  >= 0 ? p[idxCull].Trim()                    : "none",
                    DepthTest        = idxDepT  >= 0 && bool.Parse(p[idxDepT].Trim()),
                    DepthWrite       = idxDepW  >= 0 && bool.Parse(p[idxDepW].Trim()),
                    DepthCompare     = idxDepC  >= 0 ? p[idxDepC].Trim() : "less",
                    StencilTest      = idxSten  >= 0 && bool.Parse(p[idxSten].Trim()),
                    DepthBounds      = idxBnds  >= 0 && bool.Parse(p[idxBnds].Trim()),
                    PushConstantSize = idxPush  >= 0 ? uint.Parse(p[idxPush].Trim())        : 144,
                    Type             = idxType  >= 0 ? p[idxType].Trim()                    : "2d",
                    HasSampler       = idxSamp    >= 0 && p.Length > idxSamp    && bool.TryParse(p[idxSamp].Trim(),    out var hs) && hs,
                    BlendEnable      = idxBlend   >= 0 && p.Length > idxBlend   && bool.TryParse(p[idxBlend].Trim(),   out var be) && be,
                    SrcColorBlend    = idxSrcC    >= 0 && p.Length > idxSrcC    ? p[idxSrcC].Trim()    : "one",
                    DstColorBlend    = idxDstC    >= 0 && p.Length > idxDstC    ? p[idxDstC].Trim()    : "zero",
                    ColorBlendOp     = idxColOp   >= 0 && p.Length > idxColOp   ? p[idxColOp].Trim()   : "add",
                    SrcAlphaBlend    = idxSrcA    >= 0 && p.Length > idxSrcA    ? p[idxSrcA].Trim()    : "one",
                    DstAlphaBlend    = idxDstA    >= 0 && p.Length > idxDstA    ? p[idxDstA].Trim()    : "zero",
                    AlphaBlendOp     = idxAlphaOp >= 0 && p.Length > idxAlphaOp ? p[idxAlphaOp].Trim() : "add" 
                };

                Console.WriteLine($"[Shaders] {entry.Id} type={entry.Type} vert={entry.VertPath}");
                _pipelines[entry.Id] = entry;
            }
        }

        public static void BootInit()
        {
            Init(Vulkan.VK, Vulkan.Device, Vulkan.RenderPass, Vulkan.SwapExtent);
        }

        public static void Init(Vk vk, Device device, RenderPass renderPass, Extent2D extent)
        {
            _vk     = vk;
            _device = device;
            foreach (var entry in _pipelines.Values)
            {
                BuildPipeline(entry, renderPass, extent);
                _pipelineHandles[entry.Id] = entry.Handle;
                _pipelineLayouts[entry.Id] = entry.Layout;
                Console.WriteLine($"[Shaders] Stored pipeline {entry.Id} handle={entry.Handle.Handle}");
            }
            Console.WriteLine($"[Shaders] Built {_pipelines.Count} pipeline(s)");
        }

        static BlendFactor ParseBlendFactor(string s) => s.ToLower() switch
        {
            "zero"                => BlendFactor.Zero,
            "one"                 => BlendFactor.One,
            "src_alpha"           => BlendFactor.SrcAlpha,
            "one_minus_src_alpha" => BlendFactor.OneMinusSrcAlpha,
            "dst_alpha"           => BlendFactor.DstAlpha,
            "one_minus_dst_alpha" => BlendFactor.OneMinusDstAlpha,
            "src_color"           => BlendFactor.SrcColor,
            "dst_color"           => BlendFactor.DstColor,
            _                     => BlendFactor.One
        };

        static BlendOp ParseBlendOp(string s) => s.ToLower() switch
        {
            "add"              => BlendOp.Add,
            "subtract"         => BlendOp.Subtract,
            "reverse_subtract" => BlendOp.ReverseSubtract,
            "min"              => BlendOp.Min,
            "max"              => BlendOp.Max,
            _                  => BlendOp.Add
        };

        private static void BuildPipeline(Shader entry, RenderPass renderPass, Extent2D extent)
        {
            var vertCode   = File.ReadAllBytes(entry.VertPath);
            var fragCode   = File.ReadAllBytes(entry.FragPath);
            var vertModule = CreateShaderModule(vertCode);
            var fragModule = CreateShaderModule(fragCode);

            using var entryName = SilkMarshal.StringToMemory("main");

            var vertStage = new PipelineShaderStageCreateInfo
            {
                SType  = StructureType.PipelineShaderStageCreateInfo,
                Stage  = ShaderStageFlags.VertexBit,
                Module = vertModule,
                PName  = (byte*)entryName.Handle
            };
            var fragStage = new PipelineShaderStageCreateInfo
            {
                SType  = StructureType.PipelineShaderStageCreateInfo,
                Stage  = ShaderStageFlags.FragmentBit,
                Module = fragModule,
                PName  = (byte*)entryName.Handle
            };
            var stages = new[] { vertStage, fragStage };

            var bindingDesc = new VertexInputBindingDescription
            {
                Binding = 0,
                Stride = (uint)(8 * sizeof(float)),
                InputRate = VertexInputRate.Vertex
            };

            var attrPos = new VertexInputAttributeDescription
            {
                Binding = 0,
                Location = 0,
                Format = Format.R32G32B32Sfloat,
                Offset = 0
            };
            var attrNorm = new VertexInputAttributeDescription
            {
                Binding = 0,
                Location = 1,
                Format = Format.R32G32B32Sfloat,
                Offset = (uint)(3 * sizeof(float))
            };
            var attrUV = new VertexInputAttributeDescription
            {
                Binding = 0,
                Location = 2,
                Format = Format.R32G32Sfloat,
                Offset = (uint)(6 * sizeof(float))
            };

            var attrs = stackalloc VertexInputAttributeDescription[] { attrPos, attrNorm, attrUV };
            var vertexInput = new PipelineVertexInputStateCreateInfo
            {
                SType                           = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount   = 1,
                PVertexBindingDescriptions      = &bindingDesc,
                VertexAttributeDescriptionCount = 3,
                PVertexAttributeDescriptions    = attrs
            };

            var inputAssembly = new PipelineInputAssemblyStateCreateInfo
            {
                SType    = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = entry.Topology.ToLower() switch
                {
                    "triangle_strip" => PrimitiveTopology.TriangleStrip,
                    "line_list"      => PrimitiveTopology.LineList,
                    "line_strip"     => PrimitiveTopology.LineStrip,
                    "point_list"     => PrimitiveTopology.PointList,
                    _                => PrimitiveTopology.TriangleList
                }
            };

            var viewport  = new Viewport { X = 0, Y = 0, Width = extent.Width, Height = extent.Height, MinDepth = 0, MaxDepth = 1 };
            var scissor   = new Rect2D   { Offset = new Offset2D(0, 0), Extent = extent };

            var viewportState = new PipelineViewportStateCreateInfo
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1, PViewports = &viewport,
                ScissorCount  = 1, PScissors  = &scissor
            };

            var cullMode = entry.CullMode switch
            {
                "back"  => CullModeFlags.BackBit,
                "front" => CullModeFlags.FrontBit,
                _       => CullModeFlags.None
            };

            var rasterizer = new PipelineRasterizationStateCreateInfo
            {
                SType       = StructureType.PipelineRasterizationStateCreateInfo,
                PolygonMode = PolygonMode.Fill,
                CullMode    = cullMode,
                FrontFace   = FrontFace.CounterClockwise,
                LineWidth   = 1f
            };

            var multisampling = new PipelineMultisampleStateCreateInfo
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                RasterizationSamples = SampleCountFlags.Count1Bit
            };

            var depthStencil = new PipelineDepthStencilStateCreateInfo
            {
                SType            = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable  = entry.DepthTest,
                DepthWriteEnable = entry.DepthWrite,
                DepthCompareOp   = entry.DepthCompare.ToLower() switch
                {
                    "less"             => CompareOp.Less,
                    "less_or_equal"    => CompareOp.LessOrEqual,
                    "greater"          => CompareOp.Greater,
                    "greater_or_equal" => CompareOp.GreaterOrEqual,
                    "equal"            => CompareOp.Equal,
                    "never"            => CompareOp.Never,
                    "always"           => CompareOp.Always,
                    _                  => CompareOp.Less
                },
                DepthBoundsTestEnable = entry.DepthBounds,
                StencilTestEnable     = entry.StencilTest,
                MinDepthBounds   = 0f,
                MaxDepthBounds   = 1f
            };

            var colorBlendAttachment = new PipelineColorBlendAttachmentState
            {
                ColorWriteMask      = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                                      ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                BlendEnable         = entry.BlendEnable,
                SrcColorBlendFactor = ParseBlendFactor(entry.SrcColorBlend),
                DstColorBlendFactor = ParseBlendFactor(entry.DstColorBlend),
                ColorBlendOp        = ParseBlendOp(entry.ColorBlendOp),
                SrcAlphaBlendFactor = ParseBlendFactor(entry.SrcAlphaBlend),
                DstAlphaBlendFactor = ParseBlendFactor(entry.DstAlphaBlend),
                AlphaBlendOp        = ParseBlendOp(entry.AlphaBlendOp)
            };

            var colorBlend = new PipelineColorBlendStateCreateInfo
            {
                SType           = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 1,
                PAttachments    = &colorBlendAttachment
            };

            var pushRange = new PushConstantRange
            {
                StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                Offset     = 0,
                Size       = entry.PushConstantSize
            };

            if (entry.HasSampler)
            {
                var binding = new DescriptorSetLayoutBinding
                {
                    Binding         = 0,
                    DescriptorType  = DescriptorType.CombinedImageSampler,
                    DescriptorCount = 1,
                    StageFlags      = ShaderStageFlags.FragmentBit
                };
                var dsLayoutInfo = new DescriptorSetLayoutCreateInfo
                {
                    SType        = StructureType.DescriptorSetLayoutCreateInfo,
                    BindingCount = 1,
                    PBindings    = &binding
                };
                fixed (DescriptorSetLayout* pDsl = &entry.SamplerLayout)
                    _vk.CreateDescriptorSetLayout(_device, &dsLayoutInfo, null, pDsl);
            }

            fixed (DescriptorSetLayout* pDsl = &entry.SamplerLayout)
            {
                var layoutInfo = new PipelineLayoutCreateInfo
                {
                    SType                  = StructureType.PipelineLayoutCreateInfo,
                    PushConstantRangeCount = 1,
                    PPushConstantRanges    = &pushRange,
                    SetLayoutCount         = entry.HasSampler ? 1u : 0u,
                    PSetLayouts            = entry.HasSampler ? pDsl : null
                };
                fixed (PipelineLayout* pl = &entry.Layout)
                {
                    var layoutResult = _vk.CreatePipelineLayout(_device, &layoutInfo, null, pl);
                    if (layoutResult != Result.Success) Console.WriteLine($"[Shaders] Layout FAILED {entry.Id}: {layoutResult}");
                }
            }

            fixed (PipelineShaderStageCreateInfo* pStages = stages)
            {
                var pipelineInfo = new GraphicsPipelineCreateInfo
                {
                    SType               = StructureType.GraphicsPipelineCreateInfo,
                    StageCount          = 2,
                    PStages             = pStages,
                    PVertexInputState   = &vertexInput,
                    PInputAssemblyState = &inputAssembly,
                    PViewportState      = &viewportState,
                    PRasterizationState = &rasterizer,
                    PMultisampleState   = &multisampling,
                    PDepthStencilState  = &depthStencil,
                    PColorBlendState    = &colorBlend,
                    Layout              = entry.Layout,
                    RenderPass          = renderPass
                };

                fixed (Silk.NET.Vulkan.Pipeline* pp = &entry.Handle)
                {
                    var pipeResult = _vk.CreateGraphicsPipelines(_device, default, 1, &pipelineInfo, null, pp);
                    Console.WriteLine($"[Shaders] Pipeline {entry.Id}: {pipeResult} handle={entry.Handle.Handle}");
                }
            }

            _vk.DestroyShaderModule(_device, vertModule, null);
            _vk.DestroyShaderModule(_device, fragModule, null);
        }

        public static void Destroy()
        {
            foreach (var entry in _pipelines.Values)
            {
                _vk.DestroyPipeline(_device, entry.Handle, null);
                _vk.DestroyPipelineLayout(_device, entry.Layout, null);
            }
            _pipelines.Clear();
        }

        private static ShaderModule CreateShaderModule(byte[] code)
        {
            ShaderModule module;
            fixed (byte* ptr = code)
            {
                var info = new ShaderModuleCreateInfo
                {
                    SType    = StructureType.ShaderModuleCreateInfo,
                    CodeSize = (nuint)code.Length,
                    PCode    = (uint*)ptr
                };
                _vk.CreateShaderModule(_device, &info, null, &module);
            }
            return module;
        }
    }
}
