using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Yxrl.Desktop;

namespace Yxrl.Views;

internal partial class ReminderEditorWindow : Window
{
    private static readonly string[] TimeFormats = ["H:mm", "HH:mm"];
    private readonly ReminderStore store;
    private readonly DateReminder? original;
    private readonly Guid id;
    private bool loading = true;

    public DateOnly ResultDate { get; private set; }

    public ReminderEditorWindow(ReminderStore store, DateOnly date, DateReminder? original)
    {
        this.store = store;
        this.original = original;
        id = original?.Id ?? Guid.NewGuid();
        ResultDate = date;
        InitializeComponent();
        DateField.Language = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
        DateField.DisplayDateStart = CalendarService.First.ToDateTime(TimeOnly.MinValue);
        DateField.DisplayDateEnd = CalendarService.Last.ToDateTime(TimeOnly.MinValue);
        DateField.SelectedDate = date.ToDateTime(TimeOnly.MinValue);
        LeadField.ItemsSource = new[] { "0", "5", "10", "15", "30", "60", "1440" };
        TitleField.MaxLength = DateReminder.TitleLimit;
        if (original is not null) {
            Title = "编辑日期提醒";
            TitleField.Text = original.Title;
            TimeField.Text = original.ScheduledAt.ToString("HH:mm", CultureInfo.InvariantCulture);
            LeadField.Text = original.LeadMinutes.ToString(CultureInfo.InvariantCulture);
            DeleteButton.Visibility = Visibility.Visible;
            DeleteButton.IsEnabled = store.ReadError is null;
        } else {
            var now = DateTime.Now;
            var near = now.AddMinutes(15);
            var isToday = date == DateOnly.FromDateTime(now);
            var crossesMidnight = isToday && near.Date != now.Date;
            TimeField.Text = isToday ? (crossesMidnight ? "23:59" : near.ToString("HH:mm", CultureInfo.InvariantCulture)) : "09:00";
            LeadField.Text = crossesMidnight ? "0" : "10";
        }
        loading = false;
        RefreshPreview();
        Loaded += (_, _) => TitleField.Focus();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (Theme.ApplyWindowChrome(this, Native.BackdropKind.Mica))
            Background = Brushes.Transparent;
        else
            SetResourceReference(BackgroundProperty, "Surface");
    }

    private DateReminder ReadDraft(string title)
    {
        if (!DateTime.TryParse(DateField.Text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var selected))
            throw new FormatException("请选择事项日期。");
        if (!TimeOnly.TryParseExact(TimeField.Text.Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            throw new FormatException("请输入有效的时间，例如 09:30。");
        if (!int.TryParse(LeadField.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int lead) ||
            lead > DateReminder.LeadLimit)
            throw new FormatException($"请输入 0 到 {DateReminder.LeadLimit} 之间的整数分钟。");

        var date = DateOnly.FromDateTime(selected);
        DateReminder draft;
        if (original is not null && original.Date == date && TimeOnly.FromDateTime(original.ScheduledAt.DateTime) == time) {
            draft = new(id, title.Trim(), original.ScheduledAt, lead);
            draft.Validate();
        } else {
            draft = DateReminder.Create(id, title, date, time, lead, TimeZoneInfo.Local);
        }
        if (draft.ScheduledAt <= DateTimeOffset.Now &&
            (original is null || original.ScheduledAt != draft.ScheduledAt || original.LeadMinutes != lead))
            throw new FormatException("事项时间已过，请选择之后的时间。");
        return draft;
    }

    private void DraftChanged(object sender, RoutedEventArgs e)
    {
        if (!loading) RefreshPreview();
    }

    private void DateInvalid(object sender, DatePickerDateValidationErrorEventArgs e)
    {
        e.ThrowException = false;
        SaveButton.IsEnabled = false;
        ShowError("请输入有效的事项日期。");
    }

    private void RefreshPreview()
    {
        try {
            var draft = ReadDraft("提醒");
            bool delivered = original?.DeliveredAt is not null && draft.ScheduledAt == original.ScheduledAt &&
                draft.LeadMinutes == original.LeadMinutes;
            var localAlert = draft.NotifyAt.ToLocalTime();
            NextAlert.Text = delivered ? $"已提醒 · 计划时间 {localAlert:yyyy-MM-dd HH:mm}"
                : draft.NotifyAt > DateTimeOffset.Now ? $"弹框时间  {localAlert:yyyy-MM-dd HH:mm}"
                : $"计划时间 {localAlert:yyyy-MM-dd HH:mm} 已过，保存后立即提醒";
            SaveButton.IsEnabled = store.ReadError is null && !string.IsNullOrWhiteSpace(TitleField.Text);
            ShowError(store.ReadError);
        } catch (FormatException ex) {
            NextAlert.Text = "填写有效日期与时间后显示弹框时间";
            SaveButton.IsEnabled = false;
            ShowError(store.ReadError ?? ex.Message);
        }
    }

    private void ShowError(string? message)
    {
        ErrorText.Text = message ?? "";
        ErrorText.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        try {
            var candidate = ReadDraft(TitleField.Text);
            store.Save(candidate);
            ResultDate = candidate.Date;
            DialogResult = true;
        } catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException) {
            ShowError(ex.Message);
        }
    }

    private void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (original is null || MessageBox.Show(this, "删除后将取消这条提醒。", "删除提醒", MessageBoxButton.OKCancel,
                MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        try {
            store.Remove(original.Id);
            DialogResult = true;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            ShowError(ex.Message);
        }
    }
}
