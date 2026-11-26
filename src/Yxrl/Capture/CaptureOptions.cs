using System.Text.Json;
using System.Windows.Input;

namespace Yxrl.Capture;

internal enum ShotAction
{
    Start,
    Clipboard,
    Pin,
    Ocr,
    Translate
}

internal sealed record ShotShortcuts(string Start = "Ctrl+1", string Clipboard = "Ctrl+2", string Pin = "Ctrl+T", string Ocr = "Shift+C", string Translate = "Ctrl+Q")
{
    public string this[ShotAction action] => action switch
    {
        ShotAction.Start => Start,
        ShotAction.Clipboard => Clipboard,
        ShotAction.Pin => Pin,
        ShotAction.Ocr => Ocr,
        ShotAction.Translate => Translate,
        _ => throw new ArgumentOutOfRangeException(nameof(action))};
    public ShotShortcuts Bind(ShotAction action, string value) => action switch
    {
        ShotAction.Start => this with
        {
            Start = value
        },
        ShotAction.Clipboard => this with
        {
            Clipboard = value
        },
        ShotAction.Pin => this with
        {
            Pin = value
        },
        ShotAction.Ocr => this with
        {
            Ocr = value
        },
        ShotAction.Translate => this with
        {
            Translate = value
        },
        _ => throw new ArgumentOutOfRangeException(nameof(action))};
}

internal sealed record CaptureOptions
{
    public bool Enabled { get; init; } = true;
    public ShotShortcuts Shortcuts { get; init; } = new();

    public static string Label(ShotAction action) => action switch
    {
        ShotAction.Start => "开始截图",
        ShotAction.Clipboard => "钉住剪贴板图片",
        ShotAction.Pin => "钉住选区",
        ShotAction.Ocr => "提取文字",
        _ => "打开翻译"
    };
    public void Validate()
    {
        if (Shortcuts is null)
            throw new JsonException("缺少截图快捷键配置");
        var used = new HashSet<ShotChord>();
        foreach (var action in Enum.GetValues<ShotAction>())
        {
            var value = Shortcuts[action];
            if (value == "")
                continue;
            if (!ShotChord.TryParse(value, out var key))
                throw new FormatException($"「{Label(action)}」需要修饰键搭配字母、数字或功能键。");
            if (!used.Add(key))
                throw new FormatException($"「{Label(action)}」的快捷键与其他截图动作重复。");
            if (key.ToString() is "Ctrl+C" or "Ctrl+S" or "Ctrl+Z" or "Ctrl+Y")
                throw new FormatException($"「{Label(action)}」与编辑和保存快捷键重复。");
        }
    }
}

internal readonly record struct ShotChord(ModifierKeys Mods, Key Key)
{
    public static bool FromEvent(KeyEventArgs e, out ShotChord value)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        value = new(Keyboard.Modifiers, key);
        return value.Valid;
    }

    private bool Valid => Mods != ModifierKeys.None && (Key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or >= Key.F1 and <= Key.F24);

    public static bool TryParse(string? input, out ShotChord value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(input))
            return false;
        var bits = input.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = ModifierKeys.None;
        foreach (var bit in bits[..^1])
        {
            var mod = bit.ToUpperInvariant() switch
            {
                "CTRL" => ModifierKeys.Control,
                "ALT" => ModifierKeys.Alt,
                "SHIFT" => ModifierKeys.Shift,
                "WIN" => ModifierKeys.Windows,
                _ => ModifierKeys.None
            };
            if (mod == ModifierKeys.None || modifiers.HasFlag(mod))
                return false;
            modifiers |= mod;
        }

        var last = bits[^1].ToUpperInvariant();
        if (last.Length == 1 && last[0] is >= '0' and <= '9')
            last = "D" + last;
        if (!Enum.TryParse<Key>(last, true, out var key) || !string.Equals(key.ToString(), last, StringComparison.OrdinalIgnoreCase))
            return false;
        value = new(modifiers, key);
        return value.Valid;
    }

    public override string ToString()
    {
        var bits = new List<string>(5);
        if (Mods.HasFlag(ModifierKeys.Control))
            bits.Add("Ctrl");
        if (Mods.HasFlag(ModifierKeys.Alt))
            bits.Add("Alt");
        if (Mods.HasFlag(ModifierKeys.Shift))
            bits.Add("Shift");
        if (Mods.HasFlag(ModifierKeys.Windows))
            bits.Add("Win");
        bits.Add(Key is >= Key.D0 and <= Key.D9 ? ((int)Key - (int)Key.D0).ToString() : Key.ToString());
        return string.Join("+", bits);
    }
}

internal sealed record TranslationOptions(string BaseUrl = "", string Model = "", string TargetLanguage = "简体中文")
{
    public Uri? Endpoint()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Model))
            return null;
        if (!Uri.TryCreate(BaseUrl?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.Host.Length == 0 || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new FormatException("请填写不含密钥、查询参数或片段的 HTTP(S) API 基础地址。");
        if (string.IsNullOrWhiteSpace(Model))
            throw new FormatException("请填写翻译模型名。");
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/chat/completions");
    }

    public void Validate()
    {
        if (BaseUrl is null || Model is null || string.IsNullOrWhiteSpace(TargetLanguage))
            throw new FormatException("翻译设置或目标语言无效。");
        _ = Endpoint();
    }
}
