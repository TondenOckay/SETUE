using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Core.Native;
using SDL3;
using SETUE.Core;

using VkImage     = Silk.NET.Vulkan.Image;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;
using VkBuffer    = Silk.NET.Vulkan.Buffer;

namespace SETUE
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class VulkanHandlerAttribute : Attribute
    {
        public string ObjectType { get; }
        public VulkanHandlerAttribute(string objectType) => ObjectType = objectType;
    }

    public static partial class Vulkan
    {
        public static Vk             VK                  { get; internal set; } = null!;
        public static Instance       Instance            { get; internal set; }
        public static PhysicalDevice PhysicalDevice      { get; internal set; }
        public static Device         Device              { get; internal set; }
        public static Queue          GraphicsQueue       { get; internal set; }
        public static uint           GraphicsQueueFamily { get; internal set; }

        public static KhrSwapchain  KhrSwapchain   { get; internal set; } = null!;
        public static KhrSurface    KhrSurface     { get; internal set; } = null!;
        public static SwapchainKHR  Swapchain      { get; internal set; }
        public static VkImage[]     SwapImages     { get; internal set; } = null!;
        public static Format        SwapFormat     { get; internal set; }
        public static Extent2D      SwapExtent     { get; internal set; }
        public static ImageView[]   SwapImageViews { get; internal set; } = null!;
        public static Framebuffer[] Framebuffers   { get; internal set; } = null!;
        public static VkImage[]     DepthImages     { get; internal set; } = null!;
        public static DeviceMemory[] DepthMemories    { get; internal set; } = null!;
        public static ImageView[]    DepthImageViews { get; internal set; } = null!;

        public static RenderPass RenderPass { get; internal set; }

        public static CommandPool     CommandPool    { get; internal set; }
        public static CommandBuffer[] CommandBuffers { get; internal set; } = null!;
        public static VkSemaphore[]   ImageAvailable { get; internal set; } = null!;
        public static VkSemaphore[]   RenderFinished { get; internal set; } = null!;
        public static Fence[]         InFlight       { get; internal set; } = null!;
        public static int             MaxFrames      { get; internal set; } = 2;
        public static int             CurrentFrame   { get; internal set; } = 0;
        public static uint            LastImageIndex { get; internal set; }

        public static SurfaceKHR Surface { get; internal set; }
        public static IntPtr     Window  { get; internal set; }

        public static CommandPool HelperPool      { get; internal set; }
        public static bool        HelperPoolReady { get; internal set; } = false;

        public static readonly List<(VkBuffer buf, DeviceMemory mem)> DeferredFree = new();

        public static readonly Dictionary<string, Dictionary<string, string>> Rows        = new();
        public static readonly List<Dictionary<string, string>>               RowsOrdered = new();

        static readonly Dictionary<string, Action<Dictionary<string, string>>> _handlers = new();

        public static CommandBuffer CurrentCmd => CommandBuffers[LastImageIndex];

        private static void Log(string level, string message)
        {
            try
            {
                bool shouldLog = level switch
                {
                    "load"    => Debug.ShouldLogLoad("Vulkan"),
                    "update"  => Debug.ShouldLogUpdate("Vulkan"),
                    "error"   => Debug.ShouldLogError("Vulkan"),
                    "verbose" => Debug.ShouldLogVerbose("Vulkan"),
                    _         => false
                };

                if (shouldLog)
                    Debug.Log("Vulkan", message);
                else if (level == "error")
                    Console.WriteLine($"[Vulkan] ERROR: {message}");
            }
            catch
            {
                Console.WriteLine($"[Vulkan] {message}");
            }
        }

        public static void Load()
        {
            Log("load", "Load()");
            VK = Vk.GetApi();
            LoadHandlers();
            LoadSettings();

            foreach (var row in RowsOrdered)
            {
                if (!row.TryGetValue("Enabled", out var en) || en.ToLower() != "true") continue;
                if (!row.TryGetValue("ObjectType", out var objType)) continue;
                ProcessRow(objType.ToLower(), row);
            }
        }

        static void LoadHandlers()
        {
            string path = "Render Engine/Vulkan_Helper.csv";
            if (!File.Exists(path)) { Log("error", $"Missing {path}"); return; }

            var lines  = File.ReadAllLines(path);
            var header = lines[0].Split(',');

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                var cols = line.Split(',');
                var row  = new Dictionary<string, string>();
                for (int j = 0; j < header.Length && j < cols.Length; j++)
                    row[header[j].Trim()] = cols[j].Trim();

                if (!row.TryGetValue("Enabled", out var en) || en.ToLower() != "true") continue;
                if (!row.TryGetValue("Type",    out var typeName)) continue;

                var type = Type.GetType(typeName);
                if (type == null)
                {
                    Log("error", $"Handler type not found: {typeName}");
                    continue;
                }

                var methods = type
                    .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => m.GetCustomAttribute<VulkanHandlerAttribute>() != null);

                foreach (var method in methods)
                {
                    var key = method.GetCustomAttribute<VulkanHandlerAttribute>()!.ObjectType.ToLower();
                    _handlers[key] = r => method.Invoke(null, new object[] { r });
                    Log("load", $"Registered: {key} -> {typeName}.{method.Name}");
                }
            }
        }

        static void LoadSettings()
        {
            Rows.Clear();
            RowsOrdered.Clear();

            string path = "Render Engine/Vulkan.csv";
            if (!File.Exists(path)) { Log("error", $"Missing {path}"); return; }

            var lines  = File.ReadAllLines(path);
            var header = lines[0].Split(',');

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                var cols = line.Split(',');
                var row  = new Dictionary<string, string>();
                for (int j = 0; j < header.Length && j < cols.Length; j++)
                    row[header[j].Trim()] = cols[j].Trim();

                if (row.TryGetValue("ID", out var id) && !string.IsNullOrEmpty(id))
                    Rows[id] = row;

                RowsOrdered.Add(row);
            }

            Log("load", $"Config rows: {Rows.Count}");
        }

        static void ProcessRow(string objType, Dictionary<string, string> row)
        {
            if (_handlers.TryGetValue(objType, out var handler))
                handler(row);
            else
                Log("error", $"No handler for '{objType}' — create a [VulkanHandler(\"{objType}\")] method and add its class to Vulkan_Helper.csv");
        }

        public static T BindSettings<T>(Dictionary<string, string> row) where T : struct
        {
            T obj = default;
            foreach (var field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (row.TryGetValue(field.Name, out var strVal))
                {
                    var val = ConvertValue(strVal, field.FieldType);
                    if (val != null) field.SetValueDirect(__makeref(obj), val);
                }
            }
            return obj;
        }

        public static object? ConvertValue(string str, Type t)
        {
            if (t == typeof(string)) return str;
            if (t == typeof(bool))   return bool.TryParse(str,  out var b)  ? b  : false;
            if (t == typeof(int))    return int.TryParse(str,   out var iv) ? iv : 0;
            if (t == typeof(uint))   return uint.TryParse(str,  out var ui) ? ui : 0u;
            if (t == typeof(float))  return float.TryParse(str, out var f)  ? f  : 0f;
            if (t.IsEnum)            return Enum.TryParse(t, str, true, out var e) ? e : 0;
            return null;
        }

        public static void CreateSurfaceFromSDL(IntPtr window)
        {
            Window = window;
            Log("load", "Window handle stored.");
        }

        public static unsafe void DoDrawFrame()
        {
            var fence = InFlight[CurrentFrame];
            VK.WaitForFences(Device, 1, &fence, true, ulong.MaxValue);
            FlushDeferred();
            VK.ResetFences(Device, 1, &fence);

            uint imageIndex = 0;
            var acquireResult = KhrSwapchain.AcquireNextImage(Device, Swapchain, ulong.MaxValue,
                ImageAvailable[CurrentFrame], default, ref imageIndex);

            if (acquireResult == Result.ErrorOutOfDateKhr || acquireResult == Result.SuboptimalKhr)
            {
                Console.WriteLine($"[Vulkan] Swapchain out of date ({acquireResult}), recreating...");
                VulkanHandlers_Core.RecreateSwapchain();
                return;
            }
            else if (acquireResult != Result.Success)
            {
                Console.WriteLine($"[Vulkan] AcquireNextImage failed: {acquireResult}");
                return;
            }

            if (imageIndex >= CommandBuffers.Length || imageIndex >= Framebuffers.Length)
            {
                Console.WriteLine($"[Vulkan] BOUNDS ERROR: imageIndex={imageIndex}");
                return;
            }

            LastImageIndex = imageIndex;
            var cmd = CommandBuffers[imageIndex];

            var resetResult = VK.ResetCommandBuffer(cmd, 0);
            if (resetResult != Result.Success)
            {
                Console.WriteLine($"[Vulkan] ResetCommandBuffer failed: {resetResult}");
                return;
            }

            CommandBufferBeginInfo beginInfo = new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                PNext = null,
                Flags = 0
            };
            var beginResult = VK.BeginCommandBuffer(cmd, &beginInfo);
            if (beginResult != Result.Success)
            {
                Console.WriteLine($"[Vulkan] BeginCommandBuffer failed: {beginResult}");
                return;
            }

            var clearValues = stackalloc ClearValue[2];
            clearValues[0] = new ClearValue { Color = new ClearColorValue(Render.ClearR, Render.ClearG, Render.ClearB, 1.0f) };
            clearValues[1] = new ClearValue { DepthStencil = new ClearDepthStencilValue(Render.DepthClear, 0) };

            var rpBegin = new RenderPassBeginInfo
            {
                SType           = StructureType.RenderPassBeginInfo,
                RenderPass      = RenderPass,
                Framebuffer     = Framebuffers[imageIndex],
                RenderArea      = new Rect2D { Offset = new Offset2D(0, 0), Extent = SwapExtent },
                ClearValueCount = 2,
                PClearValues    = clearValues
            };
            VK.CmdBeginRenderPass(cmd, &rpBegin, SubpassContents.Inline);

            Render.RenderFrame();

            VK.CmdEndRenderPass(cmd);
            VK.EndCommandBuffer(cmd);

            var waitSem   = ImageAvailable[CurrentFrame];
            var signalSem = RenderFinished[CurrentFrame];
            var waitStage = PipelineStageFlags.ColorAttachmentOutputBit;

            var submit = new SubmitInfo
            {
                SType                = StructureType.SubmitInfo,
                WaitSemaphoreCount   = 1,
                PWaitSemaphores      = &waitSem,
                PWaitDstStageMask    = &waitStage,
                CommandBufferCount   = 1,
                PCommandBuffers      = &cmd,
                SignalSemaphoreCount = 1,
                PSignalSemaphores    = &signalSem
            };
            VK.QueueSubmit(GraphicsQueue, 1, &submit, fence);

            var sc = Swapchain;
            var present = new PresentInfoKHR
            {
                SType              = StructureType.PresentInfoKhr,
                WaitSemaphoreCount = 1,
                PWaitSemaphores    = &signalSem,
                SwapchainCount     = 1,
                PSwapchains        = &sc,
                PImageIndices      = &imageIndex
            };
            KhrSwapchain.QueuePresent(GraphicsQueue, &present);
            CurrentFrame = (CurrentFrame + 1) % MaxFrames;
        }

        public static void CreateSurface()
        {
            CreateSurfaceFromSDL(SETUE.Window.GetHandle());
        }
    }
}
