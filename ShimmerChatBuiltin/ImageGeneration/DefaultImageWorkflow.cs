using Newtonsoft.Json.Linq;

namespace ShimmerChatBuiltin.ImageGeneration
{
    /// <summary>
    /// 默认工作流。使用 ComfyUI 核心节点组成的最短可运行图：
    /// CheckpointLoaderSimple → CLIPTextEncode(正/负) + EmptyLatentImage → KSampler → VAEDecode → SaveImage。
    /// <para>
    /// 这里用代码构建而不是模板：默认工作流的参数集合是固定的强类型字段，
    /// 直接用 JObject 构建可以避免模板转义问题；「自定义工作流」才使用模板引擎。
    /// </para>
    /// </summary>
    internal static class DefaultImageWorkflow
    {
        public const string SaveImagePrefix = "ShimmerChat";

        public static JObject Build(
            string model,
            string prompt,
            string negativePrompt,
            int width,
            int height,
            int steps,
            double cfgScale,
            string sampler,
            string scheduler,
            long seed,
            int batchSize)
        {
            return new JObject
            {
                ["3"] = new JObject
                {
                    ["class_type"] = "KSampler",
                    ["inputs"] = new JObject
                    {
                        ["seed"] = seed,
                        ["steps"] = steps,
                        ["cfg"] = cfgScale,
                        ["sampler_name"] = sampler,
                        ["scheduler"] = scheduler,
                        ["denoise"] = 1.0,
                        ["model"] = new JArray("4", 0),
                        ["positive"] = new JArray("6", 0),
                        ["negative"] = new JArray("7", 0),
                        ["latent_image"] = new JArray("5", 0)
                    }
                },
                ["4"] = new JObject
                {
                    ["class_type"] = "CheckpointLoaderSimple",
                    ["inputs"] = new JObject
                    {
                        ["ckpt_name"] = model
                    }
                },
                ["5"] = new JObject
                {
                    ["class_type"] = "EmptyLatentImage",
                    ["inputs"] = new JObject
                    {
                        ["width"] = width,
                        ["height"] = height,
                        ["batch_size"] = batchSize
                    }
                },
                ["6"] = new JObject
                {
                    ["class_type"] = "CLIPTextEncode",
                    ["inputs"] = new JObject
                    {
                        ["text"] = prompt,
                        ["clip"] = new JArray("4", 1)
                    }
                },
                ["7"] = new JObject
                {
                    ["class_type"] = "CLIPTextEncode",
                    ["inputs"] = new JObject
                    {
                        ["text"] = negativePrompt,
                        ["clip"] = new JArray("4", 1)
                    }
                },
                ["8"] = new JObject
                {
                    ["class_type"] = "VAEDecode",
                    ["inputs"] = new JObject
                    {
                        ["samples"] = new JArray("3", 0),
                        ["vae"] = new JArray("4", 2)
                    }
                },
                ["9"] = new JObject
                {
                    ["class_type"] = "SaveImage",
                    ["inputs"] = new JObject
                    {
                        ["filename_prefix"] = SaveImagePrefix,
                        ["images"] = new JArray("8", 0)
                    }
                }
            };
        }
    }
}
