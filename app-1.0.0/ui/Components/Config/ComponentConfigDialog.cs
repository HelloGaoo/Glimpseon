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

// ComponentConfigDialog
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using Glimpseon.Core;

namespace Glimpseon.UI;

/// <summary>组件配置面板</summary>
public sealed class ComponentConfigDialog
{
    private readonly JsonObject _config;
    private readonly string _definitionKey;
    private readonly List<ConfigField> _fields = new();
    private readonly StackPanel _basicHost = new() { Spacing = WidgetSpace.Pad, Margin = new Thickness(0, WidgetSpace.Gap, 0, 0) };
    private readonly StackPanel _advancedHost = new() { Spacing = WidgetSpace.Pad, Margin = new Thickness(0, WidgetSpace.Gap, 0, 0) };

    public Control Content { get; }

    private ComponentConfigDialog(JsonObject config, string definitionKey)
    {
        _config = config;
        _definitionKey = definitionKey;

        var tabBasic = MakeTab("component_edit.config_basic");
        var tabAdvanced = MakeTab("component_edit.config_advanced");

        var basicScroll = new ScrollViewer { Content = _basicHost, Padding = new Thickness(0, 0, WidgetSpace.Gap, 0) };
        var advancedScroll = new ScrollViewer { Content = _advancedHost, Padding = new Thickness(0, 0, WidgetSpace.Gap, 0) };

        BuildPage(_basicHost, BasicDefs(_definitionKey));
        BuildPage(_advancedHost, AdvancedDefs(_definitionKey));

        void Select(bool basic)
        {
            basicScroll.IsVisible = basic;
            advancedScroll.IsVisible = !basic;
            tabBasic.Foreground = new SolidColorBrush(basic ? AccentColor() : FadedColor());
            tabAdvanced.Foreground = new SolidColorBrush(!basic ? AccentColor() : FadedColor());
        }
        tabBasic.PointerReleased += (_, e) => { e.Handled = true; Select(true); };
        tabAdvanced.PointerReleased += (_, e) => { e.Handled = true; Select(false); };
        Select(true);

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = WidgetSpace.Pad, Margin = new Thickness(0, 0, 0, WidgetSpace.Gap) };
        tabs.Children.Add(tabBasic);
        tabs.Children.Add(tabAdvanced);

        var pages = new Grid();
        pages.Children.Add(basicScroll);
        pages.Children.Add(advancedScroll);

        var root = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,*,Auto"),
            MinWidth = 460,
            MinHeight = 340,
        };
        Grid.SetRow(tabs, 0);
        Grid.SetRow(pages, 1);
        root.Children.Add(tabs);
        root.Children.Add(pages);

        // 重置字段, 保存生效
        var resetBtn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 12, 0, 0),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = WidgetSpace.GapSm,
                Children =
                {
                    FAIcons.Glyph(AppIcons.Refresh, 14),
                    new TextBlock
                    {
                        Text = AppUtils.Tr("component_edit.config_reset"),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
        };
        resetBtn.Click += (_, _) =>
        {
            foreach (var f in _fields)
            {
                f.Reset();
            }
        };
        Grid.SetRow(resetBtn, 2);
        root.Children.Add(resetBtn);
        Content = root;

        foreach (var f in _fields)
        {
            f.Load(_config);
        }
        Log.Debug($"[CFG] 配置面板构建: 字段数={_fields.Count}");
    }

    /// <summary>保存返回, 取消为空</summary>
    public static async Task<JsonObject?> ShowAsync(Window owner, JsonObject config, string definitionKey)
    {
        var dlg = new ComponentConfigDialog(config, definitionKey);
        var cd = new FAContentDialog
        {
            Title = AppUtils.Tr("component_edit.config_title"),
            Content = dlg.Content,
            PrimaryButtonText = AppUtils.Tr("component_edit.config_save"),
            CloseButtonText = AppUtils.Tr("component_edit.config_cancel"),
            DefaultButton = FAContentDialogButton.Primary,
        };
        var result = await cd.ShowAsync(owner);
        if (result != FAContentDialogResult.Primary)
        {
            return null;
        }
        dlg.Collect();
        return config;
    }

    private static TextBlock MakeTab(string key) => new()
    {
        Text = AppUtils.Tr(key),
        FontSize = WidgetType.Headline,
        FontWeight = WidgetWeight.Title,
        Cursor = new Cursor(StandardCursorType.Hand),
    };

    private static Color AccentColor()
    {
        try
        {
            return Color.Parse(Config.ThemeColor.Value);
        }
        catch
        {
            return Color.Parse("#30c361");
        }
    }

    private static Color FadedColor() => Color.FromArgb(120, 128, 128, 128);

    /// <summary>基础页业务字段</summary>
    private static List<(string? Title, List<ConfigField> Fields)> BasicDefs(string definitionKey)
    {
        var groups = new List<(string? Title, List<ConfigField> Fields)>();
        switch (definitionKey)
        {
            case "clock_digital":
                groups.Add((AppUtils.Tr("component_edit.group_display"), new List<ConfigField>
                {
                    new SwitchField("show_seconds", AppUtils.Tr("component_edit.config_show_seconds"), true),
                    new SwitchField("show_lunar", AppUtils.Tr("component_edit.config_show_lunar"), true),
                }));
                break;
            case "clock_square_2":
                groups.Add((AppUtils.Tr("component_edit.group_display"), new List<ConfigField>
                {
                    new SwitchField("white_face", AppUtils.Tr("component_edit.config_white_face"), true),
                }));
                break;
            case "weather_icon_temp":
                groups.Add((AppUtils.Tr("component_edit.group_display"), new List<ConfigField>
                {
                    new SwitchField("show_icon", AppUtils.Tr("component_edit.config_show_icon"), true),
                }));
                break;
            case "countdown_event":
                groups.Add((AppUtils.Tr("component_edit.group_countdown"), new List<ConfigField>
                {
                    new TextField("event_name", AppUtils.Tr("component_edit.config_event_name"), "", "", true),
                    new TextField("target_time", AppUtils.Tr("component_edit.config_target_time"), "",
                        AppUtils.Tr("component_edit.config_target_time_placeholder"), true),
                }));
                break;
            case "countdown_days":
                groups.Add((AppUtils.Tr("component_edit.group_countdown"), new List<ConfigField>
                {
                    new TextField("event_name", AppUtils.Tr("component_edit.config_event_name"), "", "", true),
                    new TextField("target_date", AppUtils.Tr("component_edit.config_target_date"), "",
                        "2026-12-31", true),
                    new PlainColorField("title_bg_color", AppUtils.Tr("component_edit.config_title_bg"), "#F98E1B"),
                }));
                break;
            case "school_info_class_info":
                groups.Add((AppUtils.Tr("component_edit.group_info"), new List<ConfigField>
                {
                    new TextField("school", AppUtils.Tr("component_edit.config_school"), "", "", true),
                    new TextField("class", AppUtils.Tr("component_edit.config_class"), "", "", true),
                }));
                break;
            case "linkage_timetable_nowlesson":
                groups.Add((AppUtils.Tr("component_edit.group_display"), new List<ConfigField>
                {
                    new SwitchField("show_teacher", AppUtils.Tr("component_edit.config_show_teacher"), true),
                    new SwitchField("show_next", AppUtils.Tr("component_edit.config_show_next"), true),
                    new SwitchField("show_duration", AppUtils.Tr("component_edit.config_show_duration"), true),
                    new SwitchField("show_countdown", AppUtils.Tr("component_edit.config_show_countdown"), true),
                }));
                break;
            case "sticky_note":
                groups.Add((AppUtils.Tr("component_edit.group_content"), new List<ConfigField>
                {
                    new ComboField("color", AppUtils.Tr("component_edit.config_note_color"),
                        new[]
                        {
                            ("yellow", AppUtils.Tr("component_edit.note_yellow")),
                            ("green", AppUtils.Tr("component_edit.note_green")),
                            ("blue", AppUtils.Tr("component_edit.note_blue")),
                            ("pink", AppUtils.Tr("component_edit.note_pink")),
                            ("orange", AppUtils.Tr("component_edit.note_orange")),
                            ("purple", AppUtils.Tr("component_edit.note_purple")),
                        }, "yellow"),
                }));
                break;
            case "clock_flip":
                groups.Add((AppUtils.Tr("component_edit.group_display"), new List<ConfigField>
                {
                    new SwitchField("show_seconds", AppUtils.Tr("component_edit.config_show_seconds"), true),
                }));
                break;
            case "clock_round":
                groups.Add((AppUtils.Tr("component_edit.group_display"), new List<ConfigField>
                {
                    new SwitchField("show_seconds", AppUtils.Tr("component_edit.config_show_second_hand"), true),
                }));
                break;
            case "clock_zones":
                groups.Add((AppUtils.Tr("component_edit.group_timezone"), new List<ConfigField>
                {
                    new TextField("zones", AppUtils.Tr("component_edit.config_zones"), ComponentDefaults.TimeZones,
                        AppUtils.Tr("component_edit.config_zones_hint")),
                }));
                break;
            case "clock_dayprogress":
                groups.Add((AppUtils.Tr("component_edit.group_display"), new List<ConfigField>
                {
                    new SwitchField("show_left", AppUtils.Tr("component_edit.config_show_left"), true),
                }));
                break;
            case "tools_search":
                groups.Add((AppUtils.Tr("component_edit.group_search"), new List<ConfigField>
                {
                    new ComboField("engine", AppUtils.Tr("component_edit.config_engine"), QuickSearchWidget.EngineOptions, "bing"),
                    new TextField("placeholder", AppUtils.Tr("component_edit.config_placeholder"),
                        AppUtils.Tr("component_edit.search_placeholder")),
                }));
                break;
            case "tools_text":
                groups.Add((AppUtils.Tr("component_edit.group_content"), new List<ConfigField>
                {
                    new TextField("text", AppUtils.Tr("component_edit.config_text"), ComponentDefaults.Text),
                    new ComboField("align", AppUtils.Tr("component_edit.config_align"),
                        new[]
                        {
                            ("left", AppUtils.Tr("component_edit.align_left")),
                            ("center", AppUtils.Tr("component_edit.align_center")),
                            ("right", AppUtils.Tr("component_edit.align_right")),
                        }, "center"),
                    new SliderField("size", AppUtils.Tr("component_edit.config_size"), ComponentDefaults.TextSize, 12, 72, "px"),
                }));
                break;
            case "info_exchange":
                groups.Add((AppUtils.Tr("component_edit.group_currency"), new List<ConfigField>
                {
                    new TextField("currencies", AppUtils.Tr("component_edit.config_currencies"), ComponentDefaults.Currencies,
                        AppUtils.Tr("component_edit.config_list_six_hint")),
                }));
                break;
        }
        return groups;
    }

    /// <summary>进阶页外观字段</summary>
    private static List<(string? Title, List<ConfigField> Fields)> AdvancedDefs(string definitionKey)
    {
        var groups = new List<(string? Title, List<ConfigField> Fields)>
        {
            (AppUtils.Tr("component_edit.group_appearance"), new List<ConfigField>
            {
                new SliderField("bg_opacity", AppUtils.Tr("component_edit.config_bg_opacity"),
                    Config.ComponentCardOpacity.Value, 0, 100, "%"),
                new SliderField("corner_radius", AppUtils.Tr("component_edit.config_corner_radius"),
                    Config.ComponentCardRadius.Value, 0, 29, "px"),
            }),
        };
        if (definitionKey is "school_info_class_info" or "linkage_timetable_nowlesson")
        {
            groups.Add((AppUtils.Tr("component_edit.group_font"), new List<ConfigField>
            {
                new SliderField("font_scale", AppUtils.Tr("component_edit.config_font_scale"), 100, 50, 200, "%"),
            }));
        }
        return groups;
    }


    private void BuildPage(StackPanel host, List<(string? Title, List<ConfigField> Fields)> groups)
    {
        if (groups.Count == 0)
        {
            host.Children.Add(new TextBlock
            {
                Text = AppUtils.Tr("component_edit.feature_pending_desc"),
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 24, 0, 0),
                Opacity = 0.7,
            });
            return;
        }
        foreach (var (title, fields) in groups)
        {
            var group = new StackPanel { Spacing = WidgetSpace.Gap };
            if (!string.IsNullOrEmpty(title))
            {
                group.Children.Add(new TextBlock
                {
                    Text = title,
                    FontSize = WidgetType.Title,
                    FontWeight = WidgetWeight.Title,
                });
            }
            var content = new StackPanel { Spacing = 12, Margin = new Thickness(12, WidgetSpace.GapXs, 0, WidgetSpace.GapXs) };
            foreach (var f in fields)
            {
                content.Children.Add(f.Build());
                _fields.Add(f);
            }
            group.Children.Add(content);
            host.Children.Add(group);
        }
    }


    private void Collect()
    {
        foreach (var f in _fields)
        {
            f.Save(_config);
        }
        Log.Debug($"[CFG] 配置已收集 键={string.Join(',', _config.Select(kv => kv.Key))}");
    }
}
