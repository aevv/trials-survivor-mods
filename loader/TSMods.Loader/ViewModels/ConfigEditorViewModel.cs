using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TSMods.Core.Config;

namespace TSMods.Loader.ViewModels;

public sealed partial class ConfigEditorViewModel : ObservableObject
{
    private readonly ModItemViewModel _mod;

    public ConfigEditorViewModel(ModItemViewModel mod, string path)
    {
        _mod = mod;
        Path = path;
        Load();
    }

    public string Path { get; }
    public ObservableCollection<ConfigSectionViewModel> Sections { get; } = [];
    public bool HasConfig => Sections.Count > 0;
    public string EmptyMessage => File.Exists(Path)
        ? "This mod has no settings."
        : "No config file yet. Enable the mod and launch the game once; BepInEx writes it on first load.";

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(RevertCommand))]
    public partial bool HasChanges { get; set; }

    [ObservableProperty] public partial string? Error { get; set; }

    internal void SettingChanged() => HasChanges = Sections.SelectMany(s => s.Settings).Any(s => s.IsModified);

    [RelayCommand(CanExecute = nameof(HasChanges))]
    private void Save()
    {
        Error = null;
        if (!_mod.Owner.CanChange)
        {
            Error = "Close the game first. BepInEx rewrites config files while it runs.";
            return;
        }

        var settings = Sections.SelectMany(s => s.Settings).ToList();
        var invalid = settings.Where(s => s.Error is not null).ToList();
        if (invalid.Count > 0)
        {
            Error = $"Fix {invalid.Count} invalid setting(s) first.";
            return;
        }

        try
        {
            var document = ConfigDocument.Load(Path);
            var changed = 0;
            foreach (var setting in settings.Where(s => s.IsModified))
            {
                var target = document.Find(setting.Id) ?? throw new InvalidOperationException($"{setting.Id} is no longer in the file.");
                var error = document.Set(target, setting.CurrentValue);
                if (error is not null) throw new InvalidOperationException($"{setting.Id}: {error}");
                changed++;
            }
            document.Save();
            Load();
            _mod.Owner.ConfigSaved(_mod, changed);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Error = e.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(HasChanges))]
    private void Revert()
    {
        Error = null;
        Load();
    }

    private void Load()
    {
        Sections.Clear();
        if (File.Exists(Path))
        {
            var document = ConfigDocument.Load(Path);
            foreach (var group in document.Settings.GroupBy(s => s.Section))
                Sections.Add(new ConfigSectionViewModel(group.Key, group.Select(s => SettingViewModel.Create(this, s)).ToList()));
        }
        HasChanges = false;
        OnPropertyChanged(nameof(HasConfig));
        OnPropertyChanged(nameof(EmptyMessage));
    }
}

public sealed class ConfigSectionViewModel(string name, IReadOnlyList<SettingViewModel> settings)
{
    public string Name { get; } = name;
    public IReadOnlyList<SettingViewModel> Settings { get; } = settings;
}

public abstract partial class SettingViewModel : ObservableObject
{
    private readonly ConfigEditorViewModel _editor;
    private readonly string _original;

    protected SettingViewModel(ConfigEditorViewModel editor, ConfigSetting setting)
    {
        _editor = editor;
        Setting = setting;
        _original = setting.Value;
    }

    protected ConfigSetting Setting { get; }

    public string Id => Setting.Id;
    public string Key => Setting.Key;
    public string Description => Setting.Description;
    public bool HasDescription => Description.Length > 0;
    public string DefaultLabel => Setting.DefaultValue is null ? "" : $"default {Setting.DefaultValue}";
    public abstract string CurrentValue { get; }
    public bool IsModified => Normalise(CurrentValue) != Normalise(_original);
    public bool IsNonDefault => Setting.DefaultValue is not null && Normalise(CurrentValue) != Normalise(Setting.DefaultValue);

    [ObservableProperty] public partial string? Error { get; set; }

    [RelayCommand]
    private void ResetToDefault()
    {
        if (Setting.DefaultValue is not null) Apply(Setting.DefaultValue);
    }

    public static SettingViewModel Create(ConfigEditorViewModel editor, ConfigSetting setting) => setting.Kind switch
    {
        SettingKind.Bool => new BoolSettingViewModel(editor, setting),
        SettingKind.Integer or SettingKind.Decimal => new NumberSettingViewModel(editor, setting),
        SettingKind.Choice => new ChoiceSettingViewModel(editor, setting),
        SettingKind.Colour => new ColourSettingViewModel(editor, setting),
        _ => new TextSettingViewModel(editor, setting),
    };

    protected abstract void Apply(string value);

    protected void Changed()
    {
        Error = Setting.Validate(CurrentValue, out _);
        OnPropertyChanged(nameof(CurrentValue));
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(IsNonDefault));
        _editor.SettingChanged();
    }

    private string Normalise(string value) => Setting.Validate(value, out var normalised) is null ? normalised : value.Trim();
}

public sealed partial class BoolSettingViewModel : SettingViewModel
{
    public BoolSettingViewModel(ConfigEditorViewModel editor, ConfigSetting setting) : base(editor, setting) =>
        Value = bool.TryParse(setting.Value.Trim(), out var value) && value;

    [ObservableProperty] public partial bool Value { get; set; }

    public override string CurrentValue => Value ? "true" : "false";

    partial void OnValueChanged(bool value) => Changed();

    protected override void Apply(string value) => Value = bool.TryParse(value, out var b) && b;
}

public sealed partial class NumberSettingViewModel : SettingViewModel
{
    private bool _syncing;

    public NumberSettingViewModel(ConfigEditorViewModel editor, ConfigSetting setting) : base(editor, setting)
    {
        IsInteger = setting.Kind == SettingKind.Integer;
        HasRange = setting.HasRange;
        Minimum = HasRange ? double.Parse(setting.RangeMin!, CultureInfo.InvariantCulture) : 0;
        Maximum = HasRange ? double.Parse(setting.RangeMax!, CultureInfo.InvariantCulture) : 0;
        _syncing = true;
        Text = setting.Value.Trim();
        SliderValue = Parse(Text) ?? Minimum;
        _syncing = false;
    }

    public bool IsInteger { get; }
    public bool HasRange { get; }
    public double Minimum { get; }
    public double Maximum { get; }
    public double SmallStep => IsInteger ? 1 : Math.Max((Maximum - Minimum) / 100, 0.01);
    public string RangeLabel => HasRange ? $"{Format(Minimum)} - {Format(Maximum)}" : "";

    [ObservableProperty] public partial string Text { get; set; } = "";
    [ObservableProperty] public partial double SliderValue { get; set; }

    public override string CurrentValue => Text;

    partial void OnTextChanged(string value)
    {
        if (_syncing) return;
        _syncing = true;
        if (Parse(value) is { } parsed) SliderValue = parsed;
        _syncing = false;
        Changed();
    }

    partial void OnSliderValueChanged(double value)
    {
        if (_syncing) return;
        _syncing = true;
        Text = Format(IsInteger ? Math.Round(value) : Math.Round(value, 2));
        _syncing = false;
        Changed();
    }

    protected override void Apply(string value) => Text = value;

    private static double? Parse(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}

public sealed partial class ChoiceSettingViewModel : SettingViewModel
{
    public ChoiceSettingViewModel(ConfigEditorViewModel editor, ConfigSetting setting) : base(editor, setting)
    {
        Options = setting.AcceptableValues;
        Selected = Options.FirstOrDefault(o => string.Equals(o, setting.Value.Trim(), StringComparison.OrdinalIgnoreCase)) ?? setting.Value.Trim();
    }

    public IReadOnlyList<string> Options { get; }

    [ObservableProperty] public partial string? Selected { get; set; }

    public override string CurrentValue => Selected ?? "";

    partial void OnSelectedChanged(string? value) => Changed();

    protected override void Apply(string value) =>
        Selected = Options.FirstOrDefault(o => string.Equals(o, value, StringComparison.OrdinalIgnoreCase)) ?? value;
}

public sealed partial class ColourSettingViewModel : SettingViewModel
{
    public ColourSettingViewModel(ConfigEditorViewModel editor, ConfigSetting setting) : base(editor, setting) =>
        Text = setting.Value.Trim();

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Swatch))] public partial string Text { get; set; } = "";

    public IBrush Swatch => ToColour(Text) is { } colour ? new SolidColorBrush(colour) : Brushes.Transparent;

    public override string CurrentValue => Text;

    partial void OnTextChanged(string value) => Changed();

    protected override void Apply(string value) => Text = value;

    private static Color? ToColour(string text)
    {
        var hex = text.Trim().TrimStart('#');
        if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgba)) return null;
        if (hex.Length == 6) rgba = (rgba << 8) | 0xFF;
        return Color.FromArgb((byte)rgba, (byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8));
    }
}

public sealed partial class TextSettingViewModel : SettingViewModel
{
    public TextSettingViewModel(ConfigEditorViewModel editor, ConfigSetting setting) : base(editor, setting)
    {
        Text = setting.Value.Trim();
        Hint = setting.Kind == SettingKind.Flags ? $"Any of: {string.Join(", ", setting.AcceptableValues)}" : setting.SettingType ?? "";
    }

    public string Hint { get; }

    [ObservableProperty] public partial string Text { get; set; } = "";

    public override string CurrentValue => Text;

    partial void OnTextChanged(string value) => Changed();

    protected override void Apply(string value) => Text = value;
}
