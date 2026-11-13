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

// CalculatorWidget
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using Glimpseon.Core;
using System.Diagnostics;
using System.Threading.Tasks;
using System.IO;
using Avalonia.Media.Imaging;
using IOPath = System.IO.Path;
using Avalonia.Controls.Shapes;
using System.Text.RegularExpressions;
using Avalonia.Animation;
using Avalonia.Threading;
using Glimpseon.Core.Services;
using Markdown.Avalonia;
using Avalonia.Input.Platform;
using System.Runtime.InteropServices;
using AvColor = Avalonia.Media.Color;
using System.Globalization;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Avalonia.Utilities;
using Avalonia.VisualTree;


namespace Glimpseon.UI;

/// <summary>
/// 计算器
/// 语义对齐: 表达式串 数字<=15位 % -> (n/100) ± ⌫ 千分位/科学计数显示
/// </summary>
public sealed class CalculatorWidget : WidgetCardBase
{
    private readonly TextBlock _history = new()
    {
        TextAlignment = TextAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
        MaxHeight = 20,
        TextTrimming = TextTrimming.CharacterEllipsis,
        FontSize = WidgetType.Label,
        FontFamily = WidgetFonts.Num,
    };
    private readonly TextBlock _display = new()
    {
        TextAlignment = TextAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
        Text = "0",
        FontWeight = WidgetWeight.Display,
        FontFamily = WidgetFonts.Num,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly Border _screen = new()
    {
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(WidgetSpace.PadCompact, WidgetSpace.Gap, WidgetSpace.PadCompact, WidgetSpace.Gap),
    };
    private readonly List<CalcKey> _keys = new();
    private string _expression = "";
    private bool _resultShown;

    private static readonly (string Text, int Row, int Col, CalcKeyKind Kind)[] ButtonSpecs =
    {
        ("⌫", 0, 0, CalcKeyKind.Utility), ("C", 0, 1, CalcKeyKind.Utility), ("%", 0, 2, CalcKeyKind.Utility), ("÷", 0, 3, CalcKeyKind.Operator),
        ("7", 1, 0, CalcKeyKind.Digit), ("8", 1, 1, CalcKeyKind.Digit), ("9", 1, 2, CalcKeyKind.Digit), ("×", 1, 3, CalcKeyKind.Operator),
        ("4", 2, 0, CalcKeyKind.Digit), ("5", 2, 1, CalcKeyKind.Digit), ("6", 2, 2, CalcKeyKind.Digit), ("−", 2, 3, CalcKeyKind.Operator),
        ("1", 3, 0, CalcKeyKind.Digit), ("2", 3, 1, CalcKeyKind.Digit), ("3", 3, 2, CalcKeyKind.Digit), ("+", 3, 3, CalcKeyKind.Operator),
        ("±", 4, 0, CalcKeyKind.Utility), ("0", 4, 1, CalcKeyKind.Digit), (".", 4, 2, CalcKeyKind.Digit), ("=", 4, 3, CalcKeyKind.Equals),
    };

    public CalculatorWidget(ComponentDefinition definition) : base(definition)
    {
        var root = new Grid
        {
            Margin = new Thickness(WidgetSpace.Pad),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*"),
            RowSpacing = WidgetSpace.Gap,
        };

        _screen.Child = new StackPanel
        {
            Spacing = 2,
            Children = { _history, _display },
        };
        Grid.SetRow(_screen, 0);
        root.Children.Add(_screen);

        var buttons = new Grid
        {
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("*,*,*,*,*"),
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,*,*,*"),
            RowSpacing = WidgetSpace.GapSm,
            ColumnSpacing = WidgetSpace.GapSm,
            Margin = new Thickness(0, WidgetSpace.GapXs, 0, 0),
        };
        Grid.SetRow(buttons, 1);
        foreach (var (text, row, col, kind) in ButtonSpecs)
        {
            var key = new CalcKey(text, kind);
            key.Invoked += () => OnButtonClick(text);
            Grid.SetRow(key, row);
            Grid.SetColumn(key, col);
            buttons.Children.Add(key);
            _keys.Add(key);
        }
        root.Children.Add(buttons);
        Card.Child = root;
        Content = Card;

        SizeChanged += (_, _) => Relayout();
        ApplyCardStyle();
    }

    private void Relayout()
    {
        var innerW = Bounds.Width - WidgetSpace.Pad * 2;
        if (innerW <= 1)
        {
            return;
        }
        var displayW = Math.Max(40, innerW - 24);
        var prefer = Math.Clamp(innerW * 0.20, 26, 48);
        _display.FontSize = CardText.Fit(_display.Text, WidgetFonts.Num, WidgetWeight.Display,
            displayW, prefer, 18, 48);
        _display.LetterSpacing = WidgetType.DisplaySpacing(_display.FontSize);
        var keySize = Math.Clamp(Math.Round(innerW / 14.5), WidgetType.Body, 20);
        foreach (var key in _keys)
        {
            key.SetFontSize(keySize);
        }
    }

    private void OnButtonClick(string key)
    {
        var calcKey = key switch { "÷" => "/", "×" => "*", "−" => "-", _ => key };
        var m = Regex.Match(_expression, @"(-?\d+\.?\d*)$");

        switch (key)
        {
            case "C":
                _expression = "";
                _resultShown = false;
                break;
            case "⌫":
                if (_resultShown)
                {
                    _expression = "";
                    _resultShown = false;
                }
                else if (_expression.Length > 0)
                {
                    _expression = _expression[..^1];
                }
                break;
            case "=":
                Calculate();
                UpdateDisplay();
                return;
            case "±":
                ToggleSign();
                UpdateDisplay();
                return;
            case "%":
                if (m.Success)
                {
                    _expression = _expression[..m.Index] + $"({m.Groups[1].Value}/100)";
                }
                break;
            case ".":
                if (_resultShown)
                {
                    _expression = "0.";
                    _resultShown = false;
                }
                else if (!(m.Success && m.Groups[1].Value.Contains('.')))
                {
                    _expression += _expression.Length == 0 || "+-*/".Contains(_expression[^1]) ? "0." : ".";
                }
                break;
            default:
                if (char.IsDigit(key[0]))
                {
                    if (_resultShown)
                    {
                        _expression = calcKey;
                        _resultShown = false;
                    }
                    else if (!m.Success
                        || m.Groups[1].Value.TrimStart('-').Replace(".", "").Length < 15)
                    {
                        _expression += calcKey;
                    }
                }
                else if ("+-*/".Contains(calcKey))
                {
                    AppendOperator(calcKey);
                    _resultShown = false;
                }
                break;
        }
        UpdateDisplay();
    }

    private void AppendOperator(string op)
    {
        if (_expression.Length == 0)
        {
            if (op == "-")
            {
                _expression = "-";
            }
            return;
        }
        if ("+-*/".Contains(_expression[^1]))
        {
            _expression = op == "-" && "+*/".Contains(_expression[^1])
                ? _expression + "-"
                : Regex.Replace(_expression, @"[+\-*/]+$", "") + op;
        }
        else
        {
            _expression += op;
        }
    }

    private void ToggleSign()
    {
        var m = Regex.Match(_expression, @"(-?\d+\.?\d*)$");
        if (!m.Success)
        {
            return;
        }
        var num = m.Groups[1].Value;
        var flipped = num.StartsWith('-') ? num[1..] : "-" + num;
        _expression = _expression[..m.Index] + flipped;
    }

    private void Calculate()
    {
        if (_expression.Length == 0)
        {
            return;
        }
        var expr = _expression.TrimEnd('+', '-', '*', '/');
        if (expr.EndsWith('.'))
        {
            expr = expr[..^1];
        }
        if (expr.Length == 0)
        {
            _display.Text = "0";
            _expression = "";
            _resultShown = false;
            return;
        }
        expr = Regex.Replace(expr, @"(-?\d+\.?\d*)%", "($1/100)");
        var result = Eval(expr);
        if (result is null)
        {
            _display.Text = AppUtils.Tr("common.error");
            _expression = "";
            _resultShown = false;
            Relayout();
            return;
        }
        var v = result.Value;
        if (v == Math.Floor(v) && !double.IsInfinity(v))
        {
            v = Math.Floor(v);
        }
        else
        {
            v = Math.Round(v, 10);
        }
        var resultStr = v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        _history.Text = DisplayExpr(expr) + " =";
        _display.Text = FormatNumber(resultStr);
        _expression = resultStr;
        _resultShown = true;
    }

    private static string DisplayExpr(string expression)
    {
        var s = Regex.Replace(expression, @"(^|[+\-*/])-(\d+\.?\d*)", m1 => $"{m1.Groups[1].Value}(-{m1.Groups[2].Value})");
        return s.Replace("/", " ÷ ").Replace("*", " × ").Replace("-", " − ").Replace("+", " + ");
    }

    private static string FormatNumber(string numStr)
    {
        if (numStr.Contains('e') || numStr.Contains('E'))
        {
            return numStr;
        }
        var sign = numStr.StartsWith('-') ? "-" : "";
        if (sign.Length > 0)
        {
            numStr = numStr[1..];
        }
        var dot = numStr.IndexOf('.');
        var integer = dot < 0 ? numStr : numStr[..dot];
        var dec = dot < 0 ? "" : numStr[(dot + 1)..];
        if (integer.Length > 15 || integer.Length + dec.Length > 18)
        {
            try
            {
                var sci = (dot < 0 ? int.Parse(numStr) : double.Parse(numStr))
                    .ToString("e15", System.Globalization.CultureInfo.InvariantCulture);
                return sign + sci.TrimEnd('0').TrimEnd('.');
            }
            catch (Exception e)
            {

                Log.Debug($"[计算器] 数字格式化失败: {e.Message}");
                return sign + numStr;
            }
        }
        var grouped = long.Parse(integer).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        return sign + grouped + (dot >= 0 ? "." + dec : "");
    }

    // 递归下降求值 + - * / 与括号(% 已在表达式里转成 (n/100))
    private sealed class ExprParser
    {
        private readonly string _s;
        private int _i;

        public ExprParser(string s) => _s = s;

        public bool AtEnd => _i >= _s.Length;

        public double ParseExpr()
        {
            var v = ParseTerm();
            while (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-'))
            {
                var op = _s[_i++];
                var r = ParseTerm();
                v = op == '+' ? v + r : v - r;
            }
            return v;
        }

        private double ParseTerm()
        {
            var v = ParseFactor();
            while (_i < _s.Length && (_s[_i] == '*' || _s[_i] == '/'))
            {
                var op = _s[_i++];
                var r = ParseFactor();
                v = op == '*' ? v * r : v / r;
            }
            return v;
        }

        private double ParseFactor()
        {
            if (_i < _s.Length && _s[_i] == '(')
            {
                _i++;
                var v = ParseExpr();
                if (_i < _s.Length && _s[_i] == ')')
                {
                    _i++;
                }
                return v;
            }
            if (_i < _s.Length && _s[_i] == '-')
            {
                _i++;
                return -ParseFactor();
            }
            var start = _i;
            while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.'))
            {
                _i++;
            }
            return double.Parse(_s[start.._i], System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private static double? Eval(string expr)
    {
        try
        {
            var p = new ExprParser(expr);
            var v = p.ParseExpr();
            // NaN 与 ±Infinity(如 1÷0) 都判为无效 否则 ∞ 走进 FormatNumber 的 long.Parse 抛异常
            return p.AtEnd && !double.IsNaN(v) && !double.IsInfinity(v) ? v : null;
        }
        catch (Exception e)
        {

            Log.Debug($"[SW] 求值失败: {e.Message}");
            return null;
        }
    }

    private void UpdateDisplay()
    {
        if (_expression.Length == 0)
        {
            _history.Text = "";
            _display.Text = "0";
            Relayout();
            return;
        }
        var head = SplitExpression(out var current);
        _history.Text = head.Length > 0 ? DisplayExpr(head) : "";
        _display.Text = current.Length > 0 ? DisplayExpr(current) : "0";
        Relayout();
    }

    private string SplitExpression(out string current)
    {
        current = "";
        if (_expression.Length == 0)
        {
            return "";
        }
        if ("+-*/".Contains(_expression[^1]))
        {
            return _expression;
        }
        var m = Regex.Match(_expression, @"(.+?[+\-*/])(-?\d*\.?\d*)$");
        if (m.Success)
        {
            current = m.Groups[2].Value;
            return m.Groups[1].Value;
        }
        current = _expression;
        return "";
    }

    protected override void ApplyCardStyle()
    {
        base.ApplyCardStyle();
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _history.Foreground = WidgetInk.Muted(p.Text);
        _display.Foreground = WidgetInk.Fg(p.Text);
        _screen.Background = new SolidColorBrush(p.Text, CardIsDark ? 0.05 : 0.04);
        foreach (var key in _keys)
        {
            key.Apply(p, CardAccent.Value, CardIsDark);
        }
        Relayout();
    }
}
