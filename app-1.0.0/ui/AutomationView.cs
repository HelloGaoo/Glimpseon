// Glimpseon
// Copyright (C) 2026 HelloGaoo
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

// 自动化页
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Glimpseon.Core;
using FluentAvalonia.UI.Controls;

namespace Glimpseon.UI;

public class AutomationView : UserControl
{
    private const string SecTrigger = "trigger";
    private const string SecCondition = "condition";
    private const string SecAction = "action";

    private static readonly string[] WeekKeys =
        ["automation.day_1", "automation.day_2", "automation.day_3", "automation.day_4", "automation.day_5", "automation.day_6", "automation.day_7"];

    private StackPanel _recipeList = null!;
    private TextBlock _stateLabel = null!;
    private TextBlock _emptyHint = null!;
    private Button _logsButton = null!;
    private ToggleSwitch _masterSwitch = null!;
    private Action? _logsFill;

    public AutomationView()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 14,
        };

        var title = Common.MakeTitle(AppUtils.Tr("automation.title"));
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var topBar = BuildTopBar();
        Grid.SetRow(topBar, 1);
        root.Children.Add(topBar);

        _recipeList = new StackPanel { Spacing = 10 };
        _emptyHint = new TextBlock
        {
            Text = AppUtils.Tr("automation.empty"),
            Opacity = 0.55,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 40, 0, 0),
        };
        var listHost = new StackPanel { Children = { _emptyHint, _recipeList } };
        Grid.SetRow(listHost, 2);
        root.Children.Add(listHost);

        var foot = new TextBlock
        {
            Text = AppUtils.Tr("automation.foot"),
            Opacity = 0.5,
            FontSize = 12,
            Margin = new Thickness(2, 0, 0, 0),
        };
        Grid.SetRow(foot, 3);
        root.Children.Add(foot);

        Content = Common.MakePageScroll(root);

        AutomationService.RecipesChanged += RequestRefresh;
        AutomationService.LogsChanged += UpdateLogsBadge;
        AttachedToLogicalTree += (_, _) => Refresh();
        Log.Debug("[自动化页] 就绪");
    }

    private static TextBlock MakeText(string key) => new()
    {
        Text = AppUtils.Tr(key),
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 13,
    };

    private static TextBlock MakeMiniLabel(string key) => new()
    {
        Text = AppUtils.Tr(key),
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 12,
        Opacity = 0.75,
    };

    private static Button IconBtn(string glyph, string tipKey)
    {
        var btn = new Button
        {
            Content = FAIcons.IconGlyph(glyph, 14),
            Padding = new Thickness(8, 5, 8, 5),
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        ToolTip.SetTip(btn, AppUtils.Tr(tipKey));
        return btn;
    }

    private static Button MiniBtn(string glyph, string tipKey)
    {
        var btn = new Button
        {
            Content = FAIcons.IconGlyph(glyph, 12),
            Padding = new Thickness(6, 4, 6, 4),
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        ToolTip.SetTip(btn, AppUtils.Tr(tipKey));
        return btn;
    }

    // 顶栏

    private Control BuildTopBar()
    {
        var host = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        _masterSwitch = new ToggleSwitch
        {
            IsChecked = Config.AutomationEnabled.Value,
            OnContent = "",
            OffContent = "",
        };
        _masterSwitch.IsCheckedChanged += (_, _) =>
        {
            Config.AutomationEnabled.Value = _masterSwitch.IsChecked == true;
            UpdateStateLabel();
        };
        left.Children.Add(_masterSwitch);

        _stateLabel = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13,
            Opacity = 0.75,
        };
        left.Children.Add(_stateLabel);

        _logsButton = new Button
        {
            Content = MakeLogsButtonContent(),
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        _logsButton.Click += (_, _) => OpenLogs();
        left.Children.Add(_logsButton);
        Grid.SetColumn(left, 0);
        host.Children.Add(left);

        var addBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { FAIcons.IconGlyph("E710", 14), MakeText("automation.new") },
            },
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        addBtn.Click += (_, _) => OpenEditor(null);
        Grid.SetColumn(addBtn, 1);
        host.Children.Add(addBtn);
        return host;
    }

    private StackPanel MakeLogsButtonContent()
    {
        var count = AutomationService.Logs.Count;
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                FAIcons.IconGlyph("E9D9", 14),
                new TextBlock
                {
                    Text = count > 0
                        ? $"{AppUtils.Tr("automation.logs")} ({count})"
                        : AppUtils.Tr("automation.logs"),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 13,
                },
            },
        };
    }

    // 列表

    private void Refresh()
    {
        UpdateStateLabel();
        UpdateLogsBadge();
        _recipeList.Children.Clear();
        _emptyHint.IsVisible = AutomationService.Recipes.Count == 0;
        foreach (var recipe in AutomationService.Recipes)
        {
            _recipeList.Children.Add(BuildRecipeCard(recipe));
        }
    }

    private void RequestRefresh()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Refresh();
        }
        else
        {
            Dispatcher.UIThread.Post(Refresh);
        }
    }

    private void UpdateStateLabel()
    {
        _masterSwitch.IsChecked = Config.AutomationEnabled.Value;
        var enabledCount = AutomationService.Recipes.Count(r => r.Enabled);
        _stateLabel.Text = Config.AutomationEnabled.Value
            ? string.Format(AppUtils.Tr("automation.state_on"), enabledCount, AutomationService.Recipes.Count)
            : AppUtils.Tr("automation.state_off");
    }

    private void UpdateLogsBadge()
    {
        if (_logsButton is null)
        {
            return;
        }
        _logsButton.Content = MakeLogsButtonContent();
    }

    private Control BuildRecipeCard(AutomationRecipe recipe)
    {
        var card = Common.MakeCard();
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(14, 10, 14, 10),
        };

        var icon = new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(
                Common.ParseAccentColor(Config.ThemeColor.Value),
                recipe.Enabled ? 0.16 : 0.08),
            Child = FAIcons.IconGlyph("E945", 18),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var nameText = new TextBlock
        {
            Text = recipe.Name.Length > 0 ? recipe.Name : AppUtils.Tr("automation.unnamed"),
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
        };
        var trigSummary = recipe.Triggers.Count > 0
            ? string.Join(" / ", recipe.Triggers.Select(e => DescribeEntry(SecTrigger, e)))
            : AppUtils.Tr("automation.no_trigger");
        var actSummary = recipe.Actions.Count > 0
            ? string.Join(" → ", recipe.Actions.Select(e => DescribeEntry(SecAction, e)))
            : AppUtils.Tr("automation.no_action");
        var condText = recipe.Condition is null
            ? AppUtils.Tr("automation.cond_none")
            : $"{AppUtils.Tr("automation.cond_prefix")}{recipe.Condition.Children.Count}{AppUtils.Tr("automation.cond_suffix")}";
        var summary = new StackPanel
        {
            Spacing = 3,
            Margin = new Thickness(12, 0, 8, 0),
            Children =
            {
                nameText,
                new TextBlock { Text = trigSummary, FontSize = 12, Opacity = 0.72, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = $"{condText}  ·  {actSummary}", FontSize = 12, Opacity = 0.72, TextTrimming = TextTrimming.CharacterEllipsis },
            },
        };
        Grid.SetColumn(summary, 1);
        grid.Children.Add(summary);

        var ops = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var toggle = new ToggleSwitch
        {
            IsChecked = recipe.Enabled,
            OnContent = "",
            OffContent = "",
        };
        toggle.IsCheckedChanged += (_, _) =>
        {
            recipe.Enabled = toggle.IsChecked == true;
            AutomationService.Update(recipe);
            Refresh();
        };
        ops.Children.Add(toggle);

        var runBtn = IconBtn("E768", "automation.run_tip");
        runBtn.Click += (_, _) => _ = AutomationService.RunNow(recipe);
        ops.Children.Add(runBtn);

        var editBtn = IconBtn("E70F", "automation.edit_tip");
        editBtn.Click += (_, _) => OpenEditor(recipe);
        ops.Children.Add(editBtn);

        var delBtn = IconBtn("E74D", "automation.delete_tip");
        delBtn.Click += async (_, _) =>
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner is not null && await Common.Confirm(owner, AppUtils.Tr("automation.delete_title"),
                    string.Format(AppUtils.Tr("automation.delete_confirm"), recipe.Name)))
            {
                AutomationService.Remove(recipe);
            }
        };
        ops.Children.Add(delBtn);

        Grid.SetColumn(ops, 2);
        grid.Children.Add(ops);
        card.Child = grid;
        return card;
    }

    private static string DescribeEntry(string section, AutomationEntry entry)
    {
        var nameKey = section == SecTrigger
            ? AutomationRegistry.GetTrigger(entry.Id)?.NameKey ?? ""
            : AutomationRegistry.GetAction(entry.Id)?.NameKey ?? "";
        var summary = section == SecTrigger
            ? AutomationRegistry.GetTrigger(entry.Id)?.Summarize(entry.Settings) ?? ""
            : AutomationRegistry.GetAction(entry.Id)?.Summarize(entry.Settings) ?? "";
        var name = nameKey.Length > 0 ? AppUtils.Tr(nameKey) : entry.Id;
        return summary.Length > 0 ? $"{name} {summary}" : name;
    }

    // 运行记录

    private void OpenLogs()
    {
        // 单订阅
        if (_logsFill is not null)
        {
            AutomationService.LogsChanged -= _logsFill;
        }
        var listPanel = new StackPanel { Spacing = 6 };
        var host = new ScrollViewer { Content = listPanel, MaxHeight = 520 };
        void Fill()
        {
            listPanel.Children.Clear();
            if (AutomationService.Logs.Count == 0)
            {
                listPanel.Children.Add(new TextBlock
                {
                    Text = AppUtils.Tr("automation.logs_empty"),
                    Opacity = 0.55,
                    Margin = new Thickness(4, 12, 0, 0),
                });
                return;
            }
            foreach (var log in AutomationService.Logs.Reverse())
            {
                var mark = log.Ok ? "✓" : "✗";
                listPanel.Children.Add(new Border
                {
                    Padding = new Thickness(10, 6, 10, 6),
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(Colors.Gray, 0.08),
                    Child = new TextBlock
                    {
                        Inlines = new InlineCollection
                        {
                            new Run($"{log.Time:HH:mm:ss}  {mark}  {log.RecipeName}")
                            {
                                FontWeight = FontWeight.SemiBold,
                            },
                            new Run(
                                $"  [{AppUtils.Tr(log.Source)}]  {log.Ms}ms{(log.Detail.Length > 0 ? $"  {log.Detail}" : "")}")
                            {
                                FontSize = 12,
                                Foreground = new SolidColorBrush(Colors.Gray, 0.9),
                            },
                        },
                        TextWrapping = TextWrapping.Wrap,
                    },
                });
            }
        }
        _logsFill = Fill;
        AutomationService.LogsChanged += Fill;
        Fill();
        ComponentExpander.Show(this, AppUtils.Tr("automation.logs_title"), host, 640, 620);
    }

    // 编辑器

    private void OpenEditor(AutomationRecipe? source)
    {
        var isNew = source is null;
        var draft = isNew ? new AutomationRecipe() : CloneRecipe(source!);

        var rootPanel = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 12 };

        var editor = BuildEditorBody(draft);
        Grid.SetRow(editor, 0);
        rootPanel.Children.Add(editor);

        var btns = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancelBtn = new Button { Content = MakeText("automation.editor_cancel") };
        var saveBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { FAIcons.IconGlyph("E73E", 14), MakeText("automation.editor_save") },
            },
        };
        btns.Children.Add(cancelBtn);
        btns.Children.Add(saveBtn);
        Grid.SetRow(btns, 1);
        rootPanel.Children.Add(btns);

        var close = ComponentExpander.Show(this,
            isNew ? AppUtils.Tr("automation.editor_new") : string.Format(AppUtils.Tr("automation.editor_edit"), draft.Name),
            rootPanel, 980, 780);

        saveBtn.Click += (_, _) =>
        {
            draft.Name = draft.Name.Trim();
            if (draft.Name.Length == 0)
            {
                draft.Name = AppUtils.Tr("automation.unnamed");
            }
            if (isNew)
            {
                AutomationService.Add(draft);
            }
            else
            {
                source!.Name = draft.Name;
                source!.Enabled = draft.Enabled;
                source!.Triggers = draft.Triggers;
                source!.Condition = draft.Condition;
                source!.Actions = draft.Actions;
                AutomationService.Update(source);
            }
            close();
        };
        cancelBtn.Click += (_, _) => close();
    }

    private static AutomationRecipe CloneRecipe(AutomationRecipe r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        Enabled = r.Enabled,
        Triggers = r.Triggers.Select(e => e.Clone()).ToList(),
        Condition = r.Condition?.Clone(),
        Actions = r.Actions.Select(e => e.Clone()).ToList(),
    };

    private Control BuildEditorBody(AutomationRecipe draft)
    {
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(4, 2, 8, 2) };

        // 名称 启用
        var nameRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
        nameRow.Children.Add(MakeFieldLabel("automation.field_name"));
        var nameBox = new TextBox { Text = draft.Name, PlaceholderText = AppUtils.Tr("automation.field_name_hint") };
        nameBox.TextChanged += (_, _) => draft.Name = nameBox.Text ?? "";
        Grid.SetColumn(nameBox, 1);
        nameRow.Children.Add(nameBox);
        var enabledLabel = MakeFieldLabel("automation.field_enabled");
        Grid.SetColumn(enabledLabel, 2);
        enabledLabel.Margin = new Thickness(14, 0, 8, 0);
        nameRow.Children.Add(enabledLabel);
        var enabledSwitch = new ToggleSwitch { IsChecked = draft.Enabled, OnContent = "", OffContent = "" };
        enabledSwitch.IsCheckedChanged += (_, _) => draft.Enabled = enabledSwitch.IsChecked == true;
        Grid.SetColumn(enabledSwitch, 3);
        nameRow.Children.Add(enabledSwitch);
        panel.Children.Add(nameRow);

        // 触发器
        var trigRows = new StackPanel { Spacing = 8 };
        var rebuildTriggers = CreateRebuilder(SecTrigger, trigRows, draft.Triggers);
        rebuildTriggers();
        panel.Children.Add(MakeSectionHeader("E823", "automation.section_triggers", "automation.add_trigger", () =>
        {
            var info = AutomationRegistry.TriggerInfos.FirstOrDefault();
            if (info is null)
            {
                return;
            }
            draft.Triggers.Add(new AutomationEntry { Id = info.Id, Settings = CloneJson(info.Defaults) });
            rebuildTriggers();
        }));
        panel.Children.Add(trigRows);

        // 条件
        panel.Children.Add(MakeSectionHeader("E9D2", "automation.section_conditions", null, null));
        panel.Children.Add(BuildConditionEditor(draft));

        // 动作
        var actRows = new StackPanel { Spacing = 8 };
        var rebuildActions = CreateRebuilder(SecAction, actRows, draft.Actions);
        rebuildActions();
        panel.Children.Add(MakeSectionHeader("E931", "automation.section_actions", "automation.add_action", () =>
        {
            var info = AutomationRegistry.ActionInfos.FirstOrDefault();
            if (info is null)
            {
                return;
            }
            draft.Actions.Add(new AutomationEntry { Id = info.Id, Settings = CloneJson(info.Defaults) });
            rebuildActions();
        }));
        panel.Children.Add(actRows);

        return new ScrollViewer { Content = panel };
    }

    private Action CreateRebuilder(string section, Panel host, List<AutomationEntry> list) => () =>
    {
        host.Children.Clear();
        foreach (var entry in list)
        {
            host.Children.Add(BuildEntryRow(section, entry, list, () => CreateRebuilder(section, host, list)()));
        }
    };

    /// <summary>区块标题 按钮右置</summary>
    private static Control MakeSectionHeader(string glyph, string key, string? addKey, Action? onAdd)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 4, 0, 0) };
        grid.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                FAIcons.IconGlyph(glyph, 15),
                new TextBlock
                {
                    Text = AppUtils.Tr(key),
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            },
        });
        if (addKey is not null && onAdd is not null)
        {
            var addBtn = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { FAIcons.IconGlyph("E710", 13), MakeText(addKey) },
                },
                Padding = new Thickness(10, 4, 10, 4),
            };
            addBtn.Click += (_, _) => onAdd();
            Grid.SetColumn(addBtn, 1);
            grid.Children.Add(addBtn);
        }
        return grid;
    }

    /// <summary>条目行 单行流式</summary>
    private Control BuildEntryRow(string section, AutomationEntry entry, List<AutomationEntry> list, Action rebuild)
    {
        var infos = section == SecTrigger
            ? AutomationRegistry.TriggerInfos.Cast<object>().ToList()
            : AutomationRegistry.ActionInfos.Cast<object>().ToList();

        var row = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Colors.Gray, 0.06),
            Padding = new Thickness(10, 8, 10, 4),
        };
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };

        var rowIcon = new ContentControl
        {
            Content = FAIcons.IconGlyph(GlyphOf(section, entry.Id), 14),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var combo = new ComboBox
        {
            MinWidth = 150,
            MaxWidth = 300,
            Height = 32,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        foreach (var info in infos)
        {
            combo.Items.Add(ProviderItemText(info));
        }
        combo.SelectedIndex = infos.FindIndex(i => ProviderId(i) == entry.Id);

        wrap.Children.Add(WpItem(rowIcon));
        wrap.Children.Add(WpItem(combo));
        foreach (var c in AutomationSettingsEditor.BuildFlat(section, entry.Id, entry.Settings))
        {
            wrap.Children.Add(WpItem(c, "flow"));
        }

        var ops = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (section == SecAction)
        {
            var upBtn = MiniBtn("E110", "automation.move_up");
            upBtn.Click += (_, _) => MoveEntry(list, entry, -1, rebuild);
            ops.Children.Add(upBtn);
            var downBtn = MiniBtn("E70D", "automation.move_down");
            downBtn.Click += (_, _) => MoveEntry(list, entry, 1, rebuild);
            ops.Children.Add(downBtn);
        }
        var delBtn = MiniBtn("E74D", "automation.delete_tip");
        delBtn.Click += (_, _) =>
        {
            list.Remove(entry);
            rebuild();
        };
        ops.Children.Add(delBtn);
        wrap.Children.Add(WpItem(ops));

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex < 0 || combo.SelectedIndex >= infos.Count)
            {
                return;
            }
            var newId = ProviderId(infos[combo.SelectedIndex]);
            if (newId == entry.Id)
            {
                return;
            }
            entry.Id = newId;
            entry.Settings = CloneJson(DefaultsOf(section, newId));
            rowIcon.Content = FAIcons.IconGlyph(GlyphOf(section, entry.Id), 14);
            // 重建设置段
            var flowItems = wrap.Children.OfType<Border>().Where(b => b.Tag as string == "flow").ToList();
            foreach (var f in flowItems)
            {
                wrap.Children.Remove(f);
            }
            var insertAt = wrap.Children.IndexOf(wrap.Children.OfType<Border>().First(b => b.Child == ops));
            foreach (var c in AutomationSettingsEditor.BuildFlat(section, entry.Id, entry.Settings))
            {
                wrap.Children.Insert(insertAt++, WpItem(c, "flow"));
            }
        };

        row.Child = wrap;
        return row;
    }

    /// <summary>流式条目 tag=设置段</summary>
    private static Border WpItem(Control content, string? tag = null)
    {
        content.VerticalAlignment = VerticalAlignment.Center;
        return new Border
        {
            Child = content,
            Margin = new Thickness(0, 0, 8, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = tag,
        };
    }

    private static void MoveEntry(List<AutomationEntry> list, AutomationEntry entry, int delta, Action rebuild)
    {
        var index = list.IndexOf(entry);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= list.Count)
        {
            return;
        }
        list.Remove(entry);
        list.Insert(target, entry);
        rebuild();
    }

    /// <summary>提供者下拉项</summary>
    private static string ProviderItemText(object info) => info switch
    {
        TriggerInfo t => AppUtils.Tr(t.NameKey),
        ActionInfo a => AppUtils.Tr(a.NameKey),
        ConditionInfo c => AppUtils.Tr(c.NameKey),
        _ => "",
    };

    private static string ProviderId(object info) => info switch
    {
        TriggerInfo t => t.Id,
        ActionInfo a => a.Id,
        _ => "",
    };

    private static string GlyphOf(string section, string id) => section == SecTrigger
        ? AutomationRegistry.GetTrigger(id)?.Glyph ?? "E946"
        : section == "condition"
            ? AutomationRegistry.GetCondition(id)?.Glyph ?? "E946"
            : AutomationRegistry.GetAction(id)?.Glyph ?? "E946";

    private static JsonObject? DefaultsOf(string section, string id) => section == SecTrigger
        ? AutomationRegistry.GetTrigger(id)?.Defaults
        : AutomationRegistry.GetAction(id)?.Defaults;

    private static JsonObject? CloneJson(JsonObject? obj) =>
        obj is null ? null : JsonNode.Parse(obj.ToJsonString()) as JsonObject;

    private static TextBlock MakeFieldLabel(string key) => new()
    {
        Text = AppUtils.Tr(key),
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 13,
        Opacity = 0.8,
    };

    // 条件编辑器

    private Control BuildConditionEditor(AutomationRecipe draft)
    {
        var panel = new StackPanel { Spacing = 8 };

        var kindCombo = new ComboBox { MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left };
        kindCombo.Items.Add(AppUtils.Tr("automation.cond_mode_none"));
        kindCombo.Items.Add(AppUtils.Tr("automation.cond_mode_all"));
        kindCombo.Items.Add(AppUtils.Tr("automation.cond_mode_any"));
        kindCombo.SelectedIndex = draft.Condition is null ? 0 : draft.Condition.Kind == "or" ? 2 : 1;

        var leaves = new StackPanel { Spacing = 8 };
        var leavesBorder = new Border
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Colors.Gray, 0.06),
            Child = leaves,
        };
        Action rebuildLeaves = null!;
        rebuildLeaves = () =>
        {
            leaves.Children.Clear();
            if (draft.Condition is { } root)
            {
                foreach (var leaf in root.Children.ToList())
                {
                    leaves.Children.Add(BuildLeafRow(draft, root, leaf, rebuildLeaves));
                }
            }
            // 空叶子隐藏容器
            leavesBorder.IsVisible = draft.Condition is { Children.Count: > 0 };
        };

        kindCombo.SelectionChanged += (_, _) =>
        {
            draft.Condition = kindCombo.SelectedIndex switch
            {
                1 => draft.Condition is null ? new AutomationCondition { Kind = "and" } : draft.Condition,
                2 => draft.Condition is null ? new AutomationCondition { Kind = "or" } : draft.Condition,
                _ => null,
            };
            if (draft.Condition is { } root && root.Kind is not ("and" or "or"))
            {
                root.Kind = kindCombo.SelectedIndex == 2 ? "or" : "and";
            }
            rebuildLeaves();
        };
        rebuildLeaves();
        panel.Children.Add(kindCombo);
        panel.Children.Add(leavesBorder);
        panel.Children.Add(MakeAddButton("automation.add_condition", () =>
        {
            draft.Condition ??= new AutomationCondition { Kind = kindCombo.SelectedIndex == 2 ? "or" : "and" };
            if (draft.Condition.Kind is not ("and" or "or"))
            {
                draft.Condition.Kind = kindCombo.SelectedIndex == 2 ? "or" : "and";
            }
            var info = AutomationRegistry.ConditionInfos.FirstOrDefault();
            if (info is null)
            {
                return;
            }
            draft.Condition.Children.Add(new AutomationCondition
            {
                Kind = "leaf",
                Id = info.Id,
                Settings = CloneJson(info.Defaults),
            });
            rebuildLeaves();
        }));
        return panel;
    }

    private static Button MakeAddButton(string key, Action onClick)
    {
        var btn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { FAIcons.IconGlyph("E710", 13), MakeText(key) },
            },
            Padding = new Thickness(10, 4, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private Control BuildLeafRow(AutomationRecipe draft, AutomationCondition root, AutomationCondition leaf, Action rebuild)
    {
        var infos = AutomationRegistry.ConditionInfos;

        var row = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Colors.Gray, 0.08),
            Padding = new Thickness(8),
        };

        var revSwitch = new ToggleSwitch
        {
            IsChecked = leaf.Reversed,
            OnContent = "",
            OffContent = "",
        };
        ToolTip.SetTip(revSwitch, AppUtils.Tr("automation.reversed_tip"));
        var revLabel = new TextBlock
        {
            Text = AppUtils.Tr("automation.reversed_tip"),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Opacity = 0.75,
        };
        revSwitch.IsCheckedChanged += (_, _) => leaf.Reversed = revSwitch.IsChecked == true;

        var rowIcon = new ContentControl
        {
            Content = FAIcons.IconGlyph(GlyphOf(SecCondition, leaf.Id), 14),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var combo = new ComboBox
        {
            MinWidth = 150,
            MaxWidth = 300,
            Height = 32,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        foreach (var info in infos)
        {
            combo.Items.Add(ProviderItemText(info));
        }
        combo.SelectedIndex = infos.ToList().FindIndex(i => i.Id == leaf.Id);

        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        var delBtn = MiniBtn("E74D", "automation.delete_tip");
        delBtn.Click += (_, _) =>
        {
            root.Children.Remove(leaf);
            rebuild();
        };
        var delHost = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { delBtn },
        };

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex < 0 || combo.SelectedIndex >= infos.Count)
            {
                return;
            }
            var newId = infos[combo.SelectedIndex].Id;
            if (newId == leaf.Id)
            {
                return;
            }
            leaf.Id = newId;
            leaf.Settings = CloneJson(infos[combo.SelectedIndex].Defaults);
            rowIcon.Content = FAIcons.IconGlyph(GlyphOf(SecCondition, leaf.Id), 14);
            // 重建设置段
            var flowItems = wrap.Children.OfType<Border>().Where(b => b.Tag as string == "flow").ToList();
            foreach (var f in flowItems)
            {
                wrap.Children.Remove(f);
            }
            var insertAt = wrap.Children.IndexOf(wrap.Children.OfType<Border>().First(b => b.Child == delHost));
            foreach (var c in AutomationSettingsEditor.BuildFlat(SecCondition, leaf.Id, leaf.Settings))
            {
                wrap.Children.Insert(insertAt++, WpItem(c, "flow"));
            }
        };

        wrap.Children.Add(WpItem(rowIcon));
        wrap.Children.Add(WpItem(combo));
        foreach (var c in AutomationSettingsEditor.BuildFlat(SecCondition, leaf.Id, leaf.Settings))
        {
            wrap.Children.Add(WpItem(c, "flow"));
        }
        wrap.Children.Add(WpItem(revSwitch));
        wrap.Children.Add(WpItem(revLabel));
        wrap.Children.Add(WpItem(delHost));

        row.Child = wrap;
        return row;
    }
}


// 提供者设置面板
internal static class AutomationSettingsEditor
{
    private const double CtrlH = 32;

    private static readonly string[] TtsVoices =
    [
        "zh-CN-XiaoxiaoNeural",
        "zh-CN-XiaoyiNeural",
        "zh-CN-YunxiNeural",
        "zh-CN-YunjianNeural",
        "zh-CN-YunyangNeural",
        "zh-CN-YunxiaNeural",
    ];

    private static readonly string[] WeekKeys =
        ["automation.day_1", "automation.day_2", "automation.day_3", "automation.day_4", "automation.day_5", "automation.day_6", "automation.day_7"];

    public static List<Control> BuildFlat(string section, string id, JsonObject? s)
    {
        if (s is null)
        {
            return [];
        }
        return section switch
        {
            "trigger" => id switch
            {
                "time_at" => [TimeField(s, "time"), DaysField(s, "days")],
                "interval" => [MakeMiniLabel("automation.field_minutes"), NumberInline(s, "minutes", 1, 1440)],
                "startup" => [MakeMiniLabel("automation.field_delay"), NumberInline(s, "delay", 0, 600)],
                "lesson_start" or "lesson_end" => LessonIndexParts(s, "index"),
                "pre_lesson" => Concat(
                    [MakeMiniLabel("automation.field_minutes"), NumberInline(s, "minutes", 1, 30)],
                    LessonIndexParts(s, "index")),
                "idle" => [MakeMiniLabel("automation.field_idle"), NumberInline(s, "minutes", 1, 240)],
                "network" => [StateField(s, "state")],
                _ => [],
            },
            "condition" => id switch
            {
                "time_range" => Concat(
                    [MakeMiniLabel("automation.field_from"), TimeField(s, "start")],
                    [MakeMiniLabel("automation.field_to"), TimeField(s, "end")]),
                "weekday" => [DaysField(s, "days")],
                "date_range" => Concat(
                    [MakeMiniLabel("automation.field_from"), TextField(s, "start", "MM-dd", 80)],
                    [MakeMiniLabel("automation.field_to"), TextField(s, "end", "MM-dd", 80)]),
                "in_class" or "in_break" => [],
                "subject_now" => [TextField(s, "subject", "automation.field_subject_hint", 240)],
                "network_state" => [StateField(s, "state")],
                _ => [],
            },
            "action" => id switch
            {
                "notification" => Concat(
                    [TextField(s, "content", "automation.field_content_hint", 380),
                     MakeMiniLabel("automation.field_type"),
                     ComboField(s, "type", ["corner", "scroll", "fullscreen"],
                         [Tr("automation.ntype_corner"), Tr("automation.ntype_scroll"), Tr("automation.ntype_fullscreen")]),
                     MakeMiniLabel("automation.field_duration"),
                     NumberInline(s, "duration", 1, 120)],
                    [MakeMiniLabel("automation.field_font"),
                     NumberInline(s, "font_size", 8, 96),
                     BoolField(s, "speak", "automation.field_speak")]),
                "speak" => Concat(
                    [TextField(s, "text", "automation.field_text_hint", 380)],
                    [MakeMiniLabel("automation.field_voice"), VoiceField(s, "voice"),
                     MakeMiniLabel("automation.field_rate"), NumberInline(s, "rate", 50, 200)]),
                "open" => [TextField(s, "target", "automation.field_target_hint", 520)],
                "builtin" => [BuiltinField(s, "key")],
                "volume" => [MakeMiniLabel("automation.field_level"), NumberInline(s, "level", 0, 100)],
                "show_main" => [ComboField(s, "page", ["", "wallpaper", "notification", "automation"],
                    [Tr("automation.sum_page_home"), Tr("navigation.wallpaper"), Tr("navigation.notification"), Tr("navigation.automation")])],
                "minimize_main" => [],
                "recipe" => [RecipeField(s, "id")],
                "log" => [TextField(s, "text", "automation.field_log_hint", 520)],
                _ => [],
            },
            _ => [],
        };
    }

    private static List<Control> Concat(params List<Control>[] parts)
    {
        var all = new List<Control>();
        foreach (var p in parts)
        {
            all.AddRange(p);
        }
        return all;
    }

    private static string Tr(string key) => AppUtils.Tr(key);

    private static TextBlock MakeMiniLabel(string key) => new()
    {
        Text = Tr(key),
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 12,
        Opacity = 0.75,
    };

    private static string GetStr(JsonObject s, string key, string fallback = "")
    {
        try
        {
            return s[key]?.GetValue<string>() ?? fallback;
        }
        catch
        {
            try
            {
                return s[key]?.ToJsonString().Trim('"') ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }
    }

    private static int GetInt(JsonObject s, string key, int fallback)
    {
        try
        {
            return s[key]?.GetValue<int>() ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static bool GetBool(JsonObject s, string key)
    {
        try
        {
            return s[key]?.GetValue<bool>() ?? false;
        }
        catch
        {
            return false;
        }
    }

    private static TextBox TextField(JsonObject s, string key, string watermark, double width)
    {
        var tb = new TextBox
        {
            Text = GetStr(s, key),
            PlaceholderText = Tr(watermark),
            Width = width,
            Height = CtrlH,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        tb.TextChanged += (_, _) => s[key] = tb.Text ?? "";
        return tb;
    }

    private static NumericUpDown NumberInline(JsonObject s, string key, double min, double max)
    {
        var spin = new NumericUpDown
        {
            Value = GetInt(s, key, (int)min),
            Minimum = (decimal)min,
            Maximum = (decimal)max,
            Increment = 1m,
            Width = 110,
            Height = CtrlH,
        };
        spin.ValueChanged += (_, _) =>
        {
            if (spin.Value is { } v)
            {
                s[key] = (int)v;
            }
        };
        return spin;
    }

    private static Control BoolField(JsonObject s, string key, string labelKey)
    {
        var sw = new ToggleSwitch
        {
            IsChecked = GetBool(s, key),
            OnContent = "",
            OffContent = "",
        };
        sw.IsCheckedChanged += (_, _) => s[key] = sw.IsChecked == true;
        var label = MakeMiniLabel(labelKey);
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        panel.Children.Add(label);
        panel.Children.Add(sw);
        return panel;
    }

    private static ComboBox ComboField(JsonObject s, string key, string[] values, string[] labels)
    {
        var combo = new ComboBox
        {
            MinWidth = 130,
            Height = CtrlH,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        foreach (var label in labels)
        {
            combo.Items.Add(label);
        }
        var current = GetStr(s, key, values.Length > 0 ? values[0] : "");
        var index = Array.FindIndex(values, v => v == current);
        combo.SelectedIndex = index >= 0 ? index : 0;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0 && combo.SelectedIndex < values.Length)
            {
                s[key] = values[combo.SelectedIndex];
            }
        };
        return combo;
    }

    private static ComboBox StateField(JsonObject s, string key) => ComboField(s, key,
        ["up", "down"], [Tr("automation.state_up"), Tr("automation.state_down")]);

    private static TimePicker TimeField(JsonObject s, string key)
    {
        TimeOnly.TryParse(GetStr(s, key), out var t);
        var picker = new TimePicker
        {
            SelectedTime = t == default ? new TimeSpan(8, 0, 0) : new TimeSpan(t.Hour, t.Minute, 0),
            ClockIdentifier = "24HourClock",
            MinWidth = 130,
            Height = CtrlH,
        };
        picker.SelectedTimeChanged += (_, e) =>
        {
            if (e.NewTime is { } time)
            {
                s[key] = time.ToString(@"hh\:mm");
            }
        };
        return picker;
    }

    private static Control DaysField(JsonObject s, string key)
    {
        var selected = new HashSet<int>();
        if (s[key] is JsonArray arr)
        {
            foreach (var n in arr)
            {
                try
                {
                    if (n?.GetValue<int>() is int v and >= 1 and <= 7)
                    {
                        selected.Add(v);
                    }
                }
                catch
                {
                    // 忽略
                }
            }
        }
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        for (var day = 1; day <= 7; day++)
        {
            var d = day;
            var check = new CheckBox
            {
                Content = Tr(WeekKeys[day - 1]),
                IsChecked = selected.Contains(d),
                MinWidth = 0,
                Padding = new Thickness(6, 2, 6, 2),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            check.IsCheckedChanged += (_, _) =>
            {
                if (check.IsChecked == true)
                {
                    selected.Add(d);
                }
                else
                {
                    selected.Remove(d);
                }
                var arrOut = new JsonArray();
                foreach (var v in selected.OrderBy(x => x))
                {
                    arrOut.Add(v);
                }
                s[key] = arrOut;
            };
            panel.Children.Add(check);
        }
        return panel;
    }

    private static List<Control> LessonIndexParts(JsonObject s, string key)
    {
        var labels = new List<string> { Tr("automation.lesson_any") };
        for (var i = 1; i <= 12; i++)
        {
            labels.Add(string.Format(Tr("automation.lesson_no"), i));
        }
        var combo = new ComboBox
        {
            MinWidth = 130,
            Height = CtrlH,
            ItemsSource = labels,
            SelectedIndex = Math.Clamp(GetInt(s, key, 0), 0, 12),
        };
        combo.SelectionChanged += (_, _) => s[key] = Math.Max(0, combo.SelectedIndex);
        return [MakeMiniLabel("automation.field_lesson"), combo];
    }

    private static ComboBox VoiceField(JsonObject s, string key)
    {
        var combo = new ComboBox
        {
            MinWidth = 190,
            Height = CtrlH,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        foreach (var voice in TtsVoices)
        {
            combo.Items.Add(voice);
        }
        var current = GetStr(s, key, TtsVoices[0]);
        combo.SelectedIndex = Math.Max(0, Array.FindIndex(TtsVoices, v => v == current));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
            {
                s[key] = TtsVoices[combo.SelectedIndex];
            }
        };
        return combo;
    }

    private static ComboBox BuiltinField(JsonObject s, string key)
    {
        var builtins = CustomActions.Builtins;
        var combo = new ComboBox
        {
            MinWidth = 200,
            Height = CtrlH,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        foreach (var b in builtins)
        {
            combo.Items.Add(AppUtils.Tr(b.LabelKey));
        }
        var current = GetStr(s, key, "schedule");
        var idx = builtins.ToList().FindIndex(b => b.Key == current);
        combo.SelectedIndex = idx >= 0 ? idx : 0;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0 && combo.SelectedIndex < builtins.Count)
            {
                s[key] = builtins[combo.SelectedIndex].Key;
            }
        };
        return combo;
    }

    private static ComboBox RecipeField(JsonObject s, string key)
    {
        var recipes = AutomationService.Recipes;
        var combo = new ComboBox
        {
            MinWidth = 220,
            Height = CtrlH,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        foreach (var r in recipes)
        {
            combo.Items.Add(r.Name.Length > 0 ? r.Name : Tr("automation.unnamed"));
        }
        var current = GetStr(s, key);
        var idx = recipes.ToList().FindIndex(r => r.Id == current);
        combo.SelectedIndex = idx;
        combo.SelectionChanged += (_, _) =>
        {
            s[key] = combo.SelectedIndex >= 0 && combo.SelectedIndex < recipes.Count
                ? recipes[combo.SelectedIndex].Id
                : "";
        };
        return combo;
    }
}
