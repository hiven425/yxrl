using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Yxrl.Views;

internal partial class CalendarWindow
{
    private readonly ReminderStore reminderStore;
    private ContextMenu? openDateMenu;
    private ReminderEditorWindow? reminderEditor;

    private bool IsManagingReminder => openDateMenu?.IsOpen == true || reminderEditor is not null;

    public bool FocusReminderEditor()
    {
        if (reminderEditor is null) return false;
        reminderEditor.Activate();
        return true;
    }

    private void AttachReminderMenu(Button button, Grid content, DateOnly date, bool isToday)
    {
        int count = reminderStore.CountOn(date);
        if (count > 0) {
            content.Children.Add(new Ellipse {
                Width = 4, Height = 4, Fill = ColorOf(isToday ? "OnSelected" : "Accent"),
                Margin = new Thickness(5,0,0,4), HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false
            });
            button.ToolTip += $"\n{count} 条日期提醒";
        }
        var reminderHelp = count > 0 ? $"{count} 条日期提醒。右键管理。" : "右键或按 Shift+F10 新建日期提醒。";
        AutomationProperties.SetHelpText(button, reminderHelp);

        var menu = new ContextMenu();
        AutomationProperties.SetAutomationId(menu, "DateActions");
        var create = new MenuItem { Header = "新建提醒…" };
        AutomationProperties.SetAutomationId(create, "NewDateReminder");
        var manage = new MenuItem { Header = "已有提醒" };
        AutomationProperties.SetAutomationId(manage, "ManageDateReminders");
        menu.Items.Add(create);
        menu.Items.Add(manage);
        create.Click += (_, _) => {
            menu.IsOpen = false;
            EditReminder(date, null);
        };
        menu.Opened += (_, _) => {
            openDateMenu = menu;
            manage.Items.Clear();
            foreach (var saved in reminderStore.ForDate(date)) {
                string title = saved.Title.Length > 24 ? saved.Title[..24] + "…" : saved.Title;
                string status = saved.DeliveredAt is null ? "" : " · 已提醒";
                var row = new MenuItem { Header = $"{saved.ScheduledAt:HH:mm}  {title}{status}" };
                AutomationProperties.SetAutomationId(row, $"EditDateReminder-{saved.Id}");
                row.Click += (_, _) => {
                    menu.IsOpen = false;
                    EditReminder(date, saved);
                };
                manage.Items.Add(row);
            }
            manage.Visibility = manage.HasItems ? Visibility.Visible : Visibility.Collapsed;
        };
        menu.Closed += (_, _) => {
            if (openDateMenu == menu) openDateMenu = null;
            leaveTimeout.Reset();
        };
        button.ContextMenu = menu;
    }

    private void EditReminder(DateOnly date, DateReminder? reminder)
    {
        if (!presented || reminderEditor is not null) return;
        var editor = new ReminderEditorWindow(reminderStore, date, reminder) { Owner = this };
        reminderEditor = editor;
        try {
            if (editor.ShowDialog() == true) Select(editor.ResultDate);
        } finally {
            reminderEditor = null;
            leaveTimeout.Reset();
        }
    }

    private void CloseReminderMenu()
    {
        if (openDateMenu is not null) openDateMenu.IsOpen = false;
    }
}
