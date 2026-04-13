using System;
using System.Collections.Generic;
using System.Linq;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using SDL3;

using VkImage     = Silk.NET.Vulkan.Image;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace SETUE
{
    public static class VulkanHandlers_Core
    {
        [VulkanHandler("instance")]
        public static void Handle_Instance(Dictionary<string, string> row)
        {
            CreateInstance(row);
            CreateSurface();
        }

        struct InstanceSettings
        {
            public string AppName;
            public string EngineName;
            public string ApiVersion;
            public string EnabledExtensions;
            public string EnabledLayers;

            public InstanceSettings()
            {
                AppName = "App";
                EngineName = "Engine";
                ApiVersion = "1.3";
                EnabledExtensions = "";
                EnabledLayers = "";
            }
        }

        static unsafe void CreateInstance(Dictionary<string, string> row)
        {
            var s = Vulkan.BindSettings<InstanceSettings>(row);

            var sdlExts   = SDL.VulkanGetInstanceExtensions(out uint _) ?? Array.Empty<string>();
            var extraExts = s.EnabledExtensions.Split(new[] { ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var allExts   = sdlExts.Concat(extraExts).Distinct().ToArray();
            var layers    = s.EnabledLayers.Split(new[] { ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            var appNamePtr    = SilkMarshal.StringToPtr(s.AppName);
            var engineNamePtr = SilkMarshal.StringToPtr(s.EngineName);
            var extPtr        = SilkMarshal.StringArrayToPtr(allExts);
            var layerPtr      = layers.Length > 0 ? SilkMarshal.StringArrayToPtr(layers) : IntPtr.Zero;

            var appInfo = new ApplicationInfo
            {
                SType              = StructureType.ApplicationInfo,
                PApplicationName   = (byte*)appNamePtr,
                ApplicationVersion = new Version32(1, 0, 0),
                PEngineName        = (byte*)engineNamePtr,
                EngineVersion      = new Version32(1, 0, 0),
                ApiVersion         = s.ApiVersion switch
                {
                    "1.0" => Vk.Version10,
                    "1.1" => Vk.Version11,
                    "1.2" => Vk.Version12,
                    _     => Vk.Version13
                }
            };

            var createInfo = new InstanceCreateInfo
            {
                SType                   = StructureType.InstanceCreateInfo,
                PApplicationInfo        = &appInfo,
                EnabledExtensionCount   = (uint)allExts.Length,
                PpEnabledExtensionNames = (byte**)extPtr,
                EnabledLayerCount       = (uint)layers.Length,
                PpEnabledLayerNames     = layers.Length > 0 ? (byte**)layerPtr : null
            };

            var result = Vulkan.VK.CreateInstance(&createInfo, null, out var instance);
            Vulkan.Instance = instance;
            Console.WriteLine(result == Result.Success ? "[Vulkan] Instance OK" : $"[Vulkan] Instance FAILED: {result}");

            SilkMarshal.Free(appNamePtr);
            SilkMarshal.Free(engineNamePtr);
            SilkMarshal.Free(extPtr);
            if (layerPtr != IntPtr.Zero) SilkMarshal.Free(layerPtr);
        }

        static void CreateSurface()
        {
            bool ok = SDL.VulkanCreateSurface(Vulkan.Window, Vulkan.Instance.Handle, IntPtr.Zero, out var handle);
            if (!ok) Console.WriteLine($"[Vulkan] Surface FAILED: {SDL.GetError()}");
            else { Vulkan.Surface = new SurfaceKHR((ulong)handle); Console.WriteLine("[Vulkan] Surface OK"); }
        }

        [VulkanHandler("device")]
        public static void Handle_Device(Dictionary<string, string> row) => CreateDevice(row);

        struct DeviceSettings
        {
            public string DevicePreference;
            public string RequiredExtensions;

            public DeviceSettings()
            {
                DevicePreference = "discrete_gpu";
                RequiredExtensions = "VK_KHR_swapchain";
            }
        }

        static unsafe void CreateDevice(Dictionary<string, string> row)
        {
            var s = Vulkan.BindSettings<DeviceSettings>(row);

            uint count = 0;
            Vulkan.VK.EnumeratePhysicalDevices(Vulkan.Instance, ref count, null);
            var devices = new PhysicalDevice[count];
            fixed (PhysicalDevice* ptr = devices)
                Vulkan.VK.EnumeratePhysicalDevices(Vulkan.Instance, ref count, ptr);

            Vulkan.PhysicalDevice = PickDevice(devices, s.DevicePreference);
            Vulkan.VK.GetPhysicalDeviceProperties(Vulkan.PhysicalDevice, out var props);
            Console.WriteLine($"[Vulkan] GPU: {SilkMarshal.PtrToString((nint)props.DeviceName)}");

            uint qCount = 0;
            Vulkan.VK.GetPhysicalDeviceQueueFamilyProperties(Vulkan.PhysicalDevice, ref qCount, null);
            var queues = new QueueFamilyProperties[qCount];
            fixed (QueueFamilyProperties* ptr = queues)
                Vulkan.VK.GetPhysicalDeviceQueueFamilyProperties(Vulkan.PhysicalDevice, ref qCount, ptr);

            for (uint i = 0; i < qCount; i++)
                if (queues[i].QueueFlags.HasFlag(QueueFlags.GraphicsBit))
                { Vulkan.GraphicsQueueFamily = i; break; }

            float priority  = 1.0f;
            var queueCreate = new DeviceQueueCreateInfo
            {
                SType            = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = Vulkan.GraphicsQueueFamily,
                QueueCount       = 1,
                PQueuePriorities = &priority
            };

            var extList = s.RequiredExtensions
                .Split(new[] { ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var extPtr  = SilkMarshal.StringArrayToPtr(extList);

            var deviceCreate = new DeviceCreateInfo
            {
                SType                   = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount    = 1,
                PQueueCreateInfos       = &queueCreate,
                EnabledExtensionCount   = (uint)extList.Length,
                PpEnabledExtensionNames = (byte**)extPtr
            };

            var result = Vulkan.VK.CreateDevice(Vulkan.PhysicalDevice, &deviceCreate, null, out var device);
            Vulkan.Device = device;
            Console.WriteLine(result == Result.Success ? "[Vulkan] Device OK" : $"[Vulkan] Device FAILED: {result}");

            SilkMarshal.Free(extPtr);
            Vulkan.VK.GetDeviceQueue(Vulkan.Device, Vulkan.GraphicsQueueFamily, 0, out var queue);
            Vulkan.GraphicsQueue = queue;
        }

        static unsafe PhysicalDevice PickDevice(PhysicalDevice[] devices, string pref)
        {
            foreach (var d in devices)
            {
                Vulkan.VK.GetPhysicalDeviceProperties(d, out var p);
                if (pref == "discrete_gpu"   && p.DeviceType == PhysicalDeviceType.DiscreteGpu)   return d;
                if (pref == "integrated_gpu" && p.DeviceType == PhysicalDeviceType.IntegratedGpu) return d;
            }
            return devices[0];
        }

        [VulkanHandler("swapchain")]
        public static void Handle_Swapchain(Dictionary<string, string> row) => CreateSwapchain(row);

        struct SwapchainSettings
        {
            public string SurfaceFormat;
            public string ColorSpace;
            public string PresentMode;
            public string CompositeAlpha;

            public SwapchainSettings()
            {
                SurfaceFormat = "B8G8R8A8_SRGB";
                ColorSpace = "SRGB_NONLINEAR";
                PresentMode = "FIFO";
                CompositeAlpha = "OPAQUE";
            }
        }

        static unsafe void CreateSwapchain(Dictionary<string, string> row)
        {
            if (!Vulkan.VK.TryGetInstanceExtension(Vulkan.Instance, out KhrSurface khrSurface))
            { Console.WriteLine("[Vulkan] KhrSurface missing"); return; }
            Vulkan.KhrSurface = khrSurface;

            khrSurface.GetPhysicalDeviceSurfaceCapabilities(Vulkan.PhysicalDevice, Vulkan.Surface, out var caps);

            uint fmtCount = 0;
            khrSurface.GetPhysicalDeviceSurfaceFormats(Vulkan.PhysicalDevice, Vulkan.Surface, ref fmtCount, null);
            var formats = new SurfaceFormatKHR[fmtCount];
            fixed (SurfaceFormatKHR* ptr = formats)
                khrSurface.GetPhysicalDeviceSurfaceFormats(Vulkan.PhysicalDevice, Vulkan.Surface, ref fmtCount, ptr);

            var s = Vulkan.BindSettings<SwapchainSettings>(row);

            var preferredFormat = s.SurfaceFormat.ToUpper() switch
            {
                "R8G8B8A8_SRGB" => Format.R8G8B8A8Srgb,
                _               => Format.B8G8R8A8Srgb
            };
            var presentMode = s.PresentMode.ToUpper() switch
            {
                "MAILBOX"   => PresentModeKHR.MailboxKhr,
                "IMMEDIATE" => PresentModeKHR.ImmediateKhr,
                _           => PresentModeKHR.FifoKhr
            };

            var chosen = formats.FirstOrDefault(f => f.Format == preferredFormat);
            if (chosen.Format == Format.Undefined) chosen = formats[0];
            Vulkan.SwapFormat = chosen.Format;
            if (caps.CurrentExtent.Width != uint.MaxValue)
                Vulkan.SwapExtent = caps.CurrentExtent;
            else
                Vulkan.SwapExtent = caps.MinImageExtent;

            uint imageCount = caps.MinImageCount + 1;
            if (caps.MaxImageCount > 0 && imageCount > caps.MaxImageCount)
                imageCount = caps.MaxImageCount;

            var createInfo = new SwapchainCreateInfoKHR
            {
                SType            = StructureType.SwapchainCreateInfoKhr,
                Surface          = Vulkan.Surface,
                MinImageCount    = imageCount,
                ImageFormat      = Vulkan.SwapFormat,
                ImageColorSpace  = chosen.ColorSpace,
                ImageExtent      = Vulkan.SwapExtent,
                ImageArrayLayers = 1,
                ImageUsage       = ImageUsageFlags.ColorAttachmentBit,
                ImageSharingMode = SharingMode.Exclusive,
                PreTransform     = caps.CurrentTransform,
                CompositeAlpha   = CompositeAlphaFlagsKHR.OpaqueBitKhr,
                PresentMode      = presentMode,
                Clipped          = true
            };

            if (!Vulkan.VK.TryGetDeviceExtension(Vulkan.Instance, Vulkan.Device, out KhrSwapchain khrSwap))
            { Console.WriteLine("[Vulkan] KhrSwapchain missing"); return; }
            Vulkan.KhrSwapchain = khrSwap;

            khrSwap.CreateSwapchain(Vulkan.Device, &createInfo, null, out var swapchain);
            Vulkan.Swapchain = swapchain;

            uint imgCount = 0;
            khrSwap.GetSwapchainImages(Vulkan.Device, Vulkan.Swapchain, ref imgCount, null);
            var swapImages = new VkImage[imgCount];
            fixed (VkImage* ptr = swapImages)
                khrSwap.GetSwapchainImages(Vulkan.Device, Vulkan.Swapchain, ref imgCount, ptr);
            Vulkan.SwapImages = swapImages;

            Console.WriteLine($"[Vulkan] Swapchain OK images={imgCount}");
        }

        public static unsafe void RecreateSwapchain()
        {
            Console.WriteLine("[Vulkan] Recreating swapchain...");

            Vulkan.VK.DeviceWaitIdle(Vulkan.Device);

            foreach (var fb in Vulkan.Framebuffers)
                Vulkan.VK.DestroyFramebuffer(Vulkan.Device, fb, null);
            foreach (var iv in Vulkan.SwapImageViews)
                Vulkan.VK.DestroyImageView(Vulkan.Device, iv, null);

            if (Vulkan.DepthImages != null && Vulkan.DepthImages.Length > 0 && Vulkan.DepthImages[0].Handle != 0)
            {
                for (int i = 0; i < Vulkan.DepthImages.Length; i++)
                {
                    Vulkan.VK.DestroyImageView(Vulkan.Device, Vulkan.DepthImageViews[i], null);
                    Vulkan.VK.DestroyImage(Vulkan.Device, Vulkan.DepthImages[i], null);
                    Vulkan.VK.FreeMemory(Vulkan.Device, Vulkan.DepthMemories[i], null);
                }
            }

            if (Vulkan.CommandBuffers != null)
                Vulkan.VK.FreeCommandBuffers(Vulkan.Device, Vulkan.CommandPool, (uint)Vulkan.CommandBuffers.Length, Vulkan.CommandBuffers.AsSpan()[0]);

            Vulkan.KhrSwapchain.DestroySwapchain(Vulkan.Device, Vulkan.Swapchain, null);

            if (!Vulkan.Rows.TryGetValue("swapchain", out var row))
            {
                Console.WriteLine("[Vulkan] ERROR: No swapchain row found!");
                return;
            }

            CreateSwapchain(row);
            CreateFramebuffers(Vulkan.Rows["renderpass"]);
            RecreateCommandBuffers();

            Console.WriteLine("[Vulkan] Swapchain recreated successfully.");
        }

        [VulkanHandler("renderpass")]
        public static void Handle_RenderPass(Dictionary<string, string> row)
        {
            CreateRenderPass(row);
            CreateFramebuffers(row);
        }

        struct RenderPassSettings
        {
            public string Attachment0_LoadOp;
            public string Attachment0_StoreOp;
            public string Attachment0_InitialLayout;
            public string Attachment0_FinalLayout;
            public string Attachment1_Format;
            public string Attachment1_LoadOp;
            public string Attachment1_StoreOp;
            public string Attachment1_InitialLayout;
            public string Attachment1_FinalLayout;
            public string Subpass0_DepthAttachment;
            public string Dependency_SrcStage;
            public string Dependency_DstStage;

            public RenderPassSettings()
            {
                Attachment0_LoadOp = "CLEAR";
                Attachment0_StoreOp = "STORE";
                Attachment0_InitialLayout = "UNDEFINED";
                Attachment0_FinalLayout = "PRESENT_SRC_KHR";
                Attachment1_Format = "";
                Attachment1_LoadOp = "CLEAR";
                Attachment1_StoreOp = "DONTCARE";
                Attachment1_InitialLayout = "UNDEFINED";
                Attachment1_FinalLayout = "DEPTH_STENCIL_ATTACHMENT_OPTIMAL";
                Subpass0_DepthAttachment = "";
                Dependency_SrcStage = "COLOR_ATTACHMENT_OUTPUT";
                Dependency_DstStage = "COLOR_ATTACHMENT_OUTPUT";
            }
        }

        static unsafe void CreateRenderPass(Dictionary<string, string> row)
        {
            var s        = Vulkan.BindSettings<RenderPassSettings>(row);
            bool hasDepth = !string.IsNullOrEmpty(s.Attachment1_Format);

            var colorAtt = new AttachmentDescription
            {
                Format         = Vulkan.SwapFormat,
                Samples        = SampleCountFlags.Count1Bit,
                LoadOp         = ParseLoadOp(s.Attachment0_LoadOp),
                StoreOp        = ParseStoreOp(s.Attachment0_StoreOp),
                StencilLoadOp  = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout  = ParseLayout(s.Attachment0_InitialLayout),
                FinalLayout    = ParseLayout(s.Attachment0_FinalLayout)
            };

            var depthFmt = s.Attachment1_Format.ToUpper() switch
            {
                "D24_UNORM_S8_UINT" => Format.D24UnormS8Uint,
                _                   => Format.D32Sfloat
            };

            var depthAtt = new AttachmentDescription
            {
                Format         = depthFmt,
                Samples        = SampleCountFlags.Count1Bit,
                LoadOp         = ParseLoadOp(s.Attachment1_LoadOp),
                StoreOp        = ParseStoreOp(s.Attachment1_StoreOp),
                StencilLoadOp  = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout  = ParseLayout(s.Attachment1_InitialLayout),
                FinalLayout    = ParseLayout(s.Attachment1_FinalLayout)
            };

            var colorRef = new AttachmentReference { Attachment = 0, Layout = ImageLayout.ColorAttachmentOptimal };
            var depthRef = new AttachmentReference { Attachment = 1, Layout = ImageLayout.DepthStencilAttachmentOptimal };
            bool useDepth = hasDepth && s.Subpass0_DepthAttachment == "1";

            var subpass = new SubpassDescription
            {
                PipelineBindPoint       = PipelineBindPoint.Graphics,
                ColorAttachmentCount    = 1,
                PColorAttachments       = &colorRef,
                PDepthStencilAttachment = useDepth ? &depthRef : null
            };

            var srcStage = s.Dependency_SrcStage.ToUpper() == "TOP_OF_PIPE"
                ? PipelineStageFlags.TopOfPipeBit
                : PipelineStageFlags.ColorAttachmentOutputBit;

            var dep = new SubpassDependency
            {
                SrcSubpass    = Vk.SubpassExternal,
                DstSubpass    = 0,
                SrcStageMask  = srcStage | (hasDepth ? PipelineStageFlags.EarlyFragmentTestsBit : 0),
                DstStageMask  = PipelineStageFlags.ColorAttachmentOutputBit | (hasDepth ? PipelineStageFlags.EarlyFragmentTestsBit : 0),
                SrcAccessMask = AccessFlags.None,
                DstAccessMask = AccessFlags.ColorAttachmentWriteBit | (hasDepth ? AccessFlags.DepthStencilAttachmentWriteBit : 0)
            };

            var atts = stackalloc AttachmentDescription[2];
            atts[0]  = colorAtt;
            atts[1]  = depthAtt;

            var rpInfo = new RenderPassCreateInfo
            {
                SType           = StructureType.RenderPassCreateInfo,
                AttachmentCount = hasDepth ? 2u : 1u,
                PAttachments    = atts,
                SubpassCount    = 1,
                PSubpasses      = &subpass,
                DependencyCount = 1,
                PDependencies   = &dep
            };

            var result = Vulkan.VK.CreateRenderPass(Vulkan.Device, &rpInfo, null, out var rp);
            Vulkan.RenderPass = rp;
            Console.WriteLine(result == Result.Success
                ? $"[Vulkan] RenderPass OK depth={hasDepth}"
                : $"[Vulkan] RenderPass FAILED: {result}");
        }

        static unsafe void CreateFramebuffers(Dictionary<string, string> row)
        {
            var s        = Vulkan.BindSettings<RenderPassSettings>(row);
            bool hasDepth = !string.IsNullOrEmpty(s.Attachment1_Format);
            var depthFmt  = s.Attachment1_Format.ToUpper() switch
            {
                "D24_UNORM_S8_UINT" => Format.D24UnormS8Uint,
                _                   => Format.D32Sfloat
            };

            if (hasDepth)
            {
                int count = Vulkan.SwapImages.Length;
                Vulkan.DepthImages     = new VkImage[count];
                Vulkan.DepthMemories    = new DeviceMemory[count];
                Vulkan.DepthImageViews = new ImageView[count];

                for (int i = 0; i < count; i++)
                {
                    Vulkan.CreateImage(
                        Vulkan.SwapExtent.Width, Vulkan.SwapExtent.Height,
                        depthFmt, ImageTiling.Optimal,
                        ImageUsageFlags.DepthStencilAttachmentBit,
                        MemoryPropertyFlags.DeviceLocalBit,
                        out Vulkan.DepthImages[i], out Vulkan.DepthMemories[i]);
                    Vulkan.DepthImageViews[i] = Vulkan.CreateImageView(Vulkan.DepthImages[i], depthFmt, ImageAspectFlags.DepthBit);
                }
                Console.WriteLine($"[Vulkan] Depth images OK (count={count})");
            }

            var views = new ImageView[Vulkan.SwapImages.Length];
            for (int i = 0; i < views.Length; i++)
                views[i] = Vulkan.CreateImageView(Vulkan.SwapImages[i], Vulkan.SwapFormat, ImageAspectFlags.ColorBit);
            Vulkan.SwapImageViews = views;

            var fbs = new Framebuffer[Vulkan.SwapImages.Length];
            for (int i = 0; i < fbs.Length; i++)
            {
                var atts = stackalloc ImageView[2];
                atts[0]  = views[i];
                atts[1]  = hasDepth ? Vulkan.DepthImageViews[i] : default;

                var fbInfo = new FramebufferCreateInfo
                {
                    SType           = StructureType.FramebufferCreateInfo,
                    RenderPass      = Vulkan.RenderPass,
                    AttachmentCount = hasDepth ? 2u : 1u,
                    PAttachments    = atts,
                    Width           = Vulkan.SwapExtent.Width,
                    Height          = Vulkan.SwapExtent.Height,
                    Layers          = 1
                };
                Vulkan.VK.CreateFramebuffer(Vulkan.Device, &fbInfo, null, out fbs[i]);
            }
            Vulkan.Framebuffers = fbs;
            Console.WriteLine($"[Vulkan] Framebuffers OK count={fbs.Length}");
        }

        static AttachmentLoadOp  ParseLoadOp(string s)  => s.ToUpper() switch
            { "LOAD" => AttachmentLoadOp.Load, "DONTCARE" => AttachmentLoadOp.DontCare, _ => AttachmentLoadOp.Clear };
        static AttachmentStoreOp ParseStoreOp(string s) =>
            s.ToUpper() == "DONTCARE" ? AttachmentStoreOp.DontCare : AttachmentStoreOp.Store;
        static ImageLayout ParseLayout(string s) => s.ToUpper() switch
        {
            "COLOR_ATTACHMENT_OPTIMAL"         => ImageLayout.ColorAttachmentOptimal,
            "PRESENT_SRC_KHR"                  => ImageLayout.PresentSrcKhr,
            "DEPTH_STENCIL_ATTACHMENT_OPTIMAL" => ImageLayout.DepthStencilAttachmentOptimal,
            "SHADER_READ_ONLY_OPTIMAL"          => ImageLayout.ShaderReadOnlyOptimal,
            _                                  => ImageLayout.Undefined
        };

        [VulkanHandler("pipeline")]
        public static void Handle_Pipeline(Dictionary<string, string> row)
        {
            Console.WriteLine("[Vulkan] Pipeline OK");
            CreateRenderLoop(row);
        }

        struct RenderLoopSettings
        {
            public int MaxFramesInFlight;

            public RenderLoopSettings()
            {
                MaxFramesInFlight = 2;
            }
        }

        static unsafe void CreateRenderLoop(Dictionary<string, string> row)
        {
            var s = Vulkan.BindSettings<RenderLoopSettings>(row);
            Console.WriteLine($"[Vulkan] RenderLoopSettings.MaxFramesInFlight = {s.MaxFramesInFlight}");
            Vulkan.MaxFrames = s.MaxFramesInFlight > 0 ? s.MaxFramesInFlight : 2;
            Console.WriteLine($"[Vulkan] MaxFrames set to {Vulkan.MaxFrames}");

            var poolInfo = new CommandPoolCreateInfo
            {
                SType            = StructureType.CommandPoolCreateInfo,
                QueueFamilyIndex = Vulkan.GraphicsQueueFamily,
                Flags            = CommandPoolCreateFlags.TransientBit | CommandPoolCreateFlags.ResetCommandBufferBit
            };
            var result = Vulkan.VK.CreateCommandPool(Vulkan.Device, &poolInfo, null, out var cmdPool);
            Vulkan.CommandPool = cmdPool;
            if (result != Result.Success)
                Console.WriteLine($"[Vulkan] CommandPool creation failed: {result}");

            RecreateCommandBuffers();

            var imgAvail  = new VkSemaphore[Vulkan.MaxFrames];
            var renderFin = new VkSemaphore[Vulkan.MaxFrames];
            var fences    = new Fence[Vulkan.MaxFrames];

            var semInfo   = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
            var fenceInfo = new FenceCreateInfo     { SType = StructureType.FenceCreateInfo, Flags = FenceCreateFlags.SignaledBit };

            for (int i = 0; i < Vulkan.MaxFrames; i++)
            {
                Vulkan.VK.CreateSemaphore(Vulkan.Device, &semInfo,   null, out imgAvail[i]);
                Vulkan.VK.CreateSemaphore(Vulkan.Device, &semInfo,   null, out renderFin[i]);
                Vulkan.VK.CreateFence    (Vulkan.Device, &fenceInfo, null, out fences[i]);
            }
            Vulkan.ImageAvailable = imgAvail;
            Vulkan.RenderFinished = renderFin;
            Vulkan.InFlight       = fences;

            Console.WriteLine("[Vulkan] RenderLoop OK");
        }

        static unsafe void RecreateCommandBuffers()
        {
            if (Vulkan.CommandBuffers != null)
            {
                Vulkan.VK.FreeCommandBuffers(Vulkan.Device, Vulkan.CommandPool, (uint)Vulkan.CommandBuffers.Length, Vulkan.CommandBuffers[0]);
            }

            uint count = (uint)Vulkan.Framebuffers.Length;
            var cmdBufs = new CommandBuffer[count];
            var allocInfo = new CommandBufferAllocateInfo
            {
                SType              = StructureType.CommandBufferAllocateInfo,
                CommandPool        = Vulkan.CommandPool,
                Level              = CommandBufferLevel.Primary,
                CommandBufferCount = count
            };
            fixed (CommandBuffer* ptr = cmdBufs)
            {
                var res = Vulkan.VK.AllocateCommandBuffers(Vulkan.Device, &allocInfo, ptr);
                if (res != Result.Success)
                    Console.WriteLine($"[Vulkan] CommandBuffer allocation failed: {res}");
            }
            Vulkan.CommandBuffers = cmdBufs;
        }
    }
}
