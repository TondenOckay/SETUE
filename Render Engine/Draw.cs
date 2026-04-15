using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Numerics;
using Silk.NET.Vulkan;
using SETUE.ECS;
using SETUE.RenderEngine;
using static SETUE.Vulkan;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace SETUE
{
    [StructLayout(LayoutKind.Sequential)]
    struct PushConstants
    {
        public Matrix4x4 MVP;
        public Matrix4x4 Model;
        public Vector4 Color;
    }

    public static class Draw
    {
        private static bool _enabled = true;
        private static string _pipelineId = "mesh_pipeline_back";

        public static void Load()
        {
            string path = "Render Engine/Draw.csv";
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Draw] Missing {path}, using defaults");
                return;
            }

            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return;
            var headers = lines[0].Split(',');
            var values = lines[1].Split(',');

            for (int i = 0; i < headers.Length && i < values.Length; i++)
            {
                string key = headers[i].Trim();
                string val = values[i].Trim();
                switch (key)
                {
                    case "Enabled":
                        _enabled = bool.TryParse(val, out var e) && e;
                        break;
                    case "PipelineId":
                        _pipelineId = val;
                        break;
                }
            }
            Console.WriteLine($"[Draw] Loaded config: Enabled={_enabled}, Pipeline={_pipelineId}");
        }

        public static unsafe void Execute()
        {
            if (!_enabled) return;

            var cmd = CurrentCmd;
            var world = Object.ECSWorld;

            Console.WriteLine("[Draw] Pipeline handles from Shaders.All:");
            foreach (var kv in Shaders.All)
            {
                Console.WriteLine($"  {kv.Key}: handle={kv.Value.Handle.Handle}");
            }

            Console.WriteLine("[Draw] Using GetHandle for " + _pipelineId);
            var handleViaGet = Shaders.GetHandle(_pipelineId);
            Console.WriteLine($"  GetHandle returned handle={handleViaGet.Handle}");

            if (!Shaders.All.TryGetValue(_pipelineId, out var shader))
            {
                Console.WriteLine($"[Draw] Pipeline '{_pipelineId}' not found in Shaders.All");
                return;
            }

            var pipeline = shader.Handle;
            var layout = shader.Layout;
            if (pipeline.Handle == 0)
            {
                Console.WriteLine($"[Draw] Pipeline '{_pipelineId}' has zero handle (from Shaders.All)");
                return;
            }

            int drawCount3D = 0;
            foreach (var (e, mvpComp, mesh) in world.Query<MVPComponent, MeshComponent>())
            {
                drawCount3D++;
                var material = world.GetComponent<MaterialComponent>(e);

                if (!Shaders.All.TryGetValue(material.PipelineId, out var objShader))
                {
                    Console.WriteLine($"[Draw] Pipeline '{material.PipelineId}' not found for entity {e}");
                    continue;
                }
                var objPipeline = objShader.Handle;
                var objLayout   = objShader.Layout;

                VK.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, objPipeline);

                VkBuffer vbuf = new VkBuffer { Handle = (ulong)mesh.VertexBuffer };
                VkBuffer ibuf = new VkBuffer { Handle = (ulong)mesh.IndexBuffer };
                ulong offset = 0;
                VK.CmdBindVertexBuffers(cmd, 0, 1, vbuf, &offset);
                VK.CmdBindIndexBuffer(cmd, ibuf, 0, IndexType.Uint32);

                PushConstants pc = new PushConstants
                {
                    MVP = mvpComp.MVP,
                    Model = Matrix4x4.Identity,
                    Color = material.Color
                };
                VK.CmdPushConstants(cmd, objLayout, ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, 0, (uint)sizeof(PushConstants), &pc);

                VK.CmdDrawIndexed(cmd, mesh.IndexCount, 1, 0, 0, 0);
            }

            Console.WriteLine($"[Draw] Drew {drawCount3D} 3D objects");
        }

        public static unsafe void Execute2D()
        {
            var cmd = CurrentCmd;
            foreach (var c in SETUE.Scene.Scene2D.Commands)
            {
                if (!Shaders.All.TryGetValue(c.PipelineId, out var shader)) continue;
                if (shader.Handle.Handle == 0) continue;

                VK.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, shader.Handle);

                var vbuf = c.VertexBuffer;
                ulong offset = 0;
                VK.CmdBindVertexBuffers(cmd, 0, 1, vbuf, &offset);
                VK.CmdBindIndexBuffer(cmd, c.IndexBuffer, 0, IndexType.Uint32);

                // Bind font atlas descriptor set if text
                if (c.IsText && !string.IsNullOrEmpty(c.FontId))
                {
                    var font = SETUE.UI.Fonts.Get(c.FontId);
                    if (font != null && font.DescriptorSet.Handle != 0)
                    {
                        var ds = font.DescriptorSet;
                        VK.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, shader.Layout,
                            0, 1, &ds, 0, null);
                    }
                }

                PushConstants pc = new PushConstants
                {
                    MVP = c.Transform,
                    Model = Matrix4x4.Identity,
                    Color = c.Color
                };
                VK.CmdPushConstants(cmd, shader.Layout,
                    ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                    0, (uint)sizeof(PushConstants), &pc);

                VK.CmdDrawIndexed(cmd, c.IndexCount, 1, 0, 0, 0);
            }
            Console.WriteLine($"[Draw] Drew {SETUE.Scene.Scene2D.Commands.Count} 2D commands");
        }
    }
}
