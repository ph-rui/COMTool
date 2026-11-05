using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace COMTool.Core.Config;

/// <summary>
/// r52: System.Text.Json source-generation context so config (反)序列化在
/// Native AOT 下不依赖运行时反射/代码生成（IL3050/IL2026）。
/// 配置结构（ComtoolConfig 及其嵌套类型）变更后需重新编译以重生成元数据。
/// 选项与 ComtoolConfig.Save 原手写 options 保持一致（缩进 + 宽松转义）。
/// </summary>
// 注意：JsonSourceGenerationOptions 不支持自定义 Encoder（.NET 10），
// 生成器用默认编码（CJK 会转义为 \uXXXX）——JSON 往返等价，仅文件可读性略差。
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ComtoolConfig))]
public partial class ConfigJsonContext : JsonSerializerContext
{
}
