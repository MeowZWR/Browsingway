using Dalamud.Interface;
using ImGuiNET;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Browsingway;

// ReSharper disable once ClassNeverInstantiated.Global
internal class Settings : IDisposable
{
	public event EventHandler<InlayConfiguration>? OverlayAdded;
	public event EventHandler<InlayConfiguration>? OverlayNavigated;
	public event EventHandler<InlayConfiguration>? OverlayDebugged;
	public event EventHandler<InlayConfiguration>? OverlayRemoved;
	public event EventHandler<InlayConfiguration>? OverlayZoomed;
	public event EventHandler<InlayConfiguration>? OverlayMuted;
	public event EventHandler<InlayConfiguration>? OverlayUserCssChanged;
	public readonly Configuration Config;
	private bool _actAvailable = false;

#if DEBUG
	private bool _open = true;
#else
	private bool _open;
#endif

	private InlayConfiguration? _selectedOverlay;
	private Timer? _saveDebounceTimer;

	public Settings()
	{
		Services.PluginInterface.UiBuilder.OpenConfigUi += () => _open = true;
		Config = Services.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
	}

	public void Dispose() { }

	public void OnActAvailabilityChanged(bool available)
	{
		_actAvailable = available;
		foreach (InlayConfiguration? overlayConfig in Config.Inlays)
		{
			if (overlayConfig is { ActOptimizations: true, Disabled: false })
			{
				if (_actAvailable)
					OverlayAdded?.Invoke(this, overlayConfig);
				else
					OverlayRemoved?.Invoke(this, overlayConfig);
			}
		}
	}

	public void HandleConfigCommand(string rawArgs)
	{
		_open = true;

		// TODO: Add further config handling if required here.
	}

	public void HandleOverlayCommand(string rawArgs)
	{
		string[] args = rawArgs.Split(null as char[], 3, StringSplitOptions.RemoveEmptyEntries);

		// Ensure there's enough arguments
		if (args.Length < 2 || (args[1] != "reload" && args.Length < 3))
		{
			Services.Chat.PrintError("Invalid overlay command. Supported syntax: '[overlayCommandName] [setting] [value]'");
			return;
		}

		// Find the matching overlay config
		InlayConfiguration? targetConfig = Config.Inlays.Find(overlay => GetOverlayCommandName(overlay) == args[0]);
		if (targetConfig == null)
		{
			Services.Chat.PrintError(
				$"Unknown overlay '{args[0]}'.");
			return;
		}

		switch (args[1])
		{
			case "url":
				CommandSettingString(args[2], ref targetConfig.Url);
				// TODO: This call is duped with imgui handling. DRY.
				NavigateOverlay(targetConfig);
				break;
			case "locked":
				CommandSettingBoolean(args[2], ref targetConfig.Locked);
				break;
			case "hidden":
				CommandSettingBoolean(args[2], ref targetConfig.Hidden);
				break;
			case "typethrough":
				CommandSettingBoolean(args[2], ref targetConfig.TypeThrough);
				break;
			case "fullscreen":
				CommandSettingBoolean(args[2], ref targetConfig.Fullscreen);
				break;
			case "clickthrough":
				CommandSettingBoolean(args[2], ref targetConfig.ClickThrough);
				break;
			case "muted":
				CommandSettingBoolean(args[2], ref targetConfig.Muted);
				break;
			case "disabled":
				CommandSettingBoolean(args[2], ref targetConfig.Disabled);
				break;
			case "act":
				CommandSettingBoolean(args[2], ref targetConfig.ActOptimizations);
				break;
			case "reload":
				ReloadOverlay(targetConfig);
				break;

			default:
				Services.Chat.PrintError(
					$"Unknown setting '{args[1]}. Valid settings are: url,hidden,locked,fullscreen,clickthrough,typethrough,muted,disabled,act.");
				return;
		}

		SaveSettings();
	}

	private void CommandSettingString(string value, ref string target)
	{
		target = value;
	}

	private void CommandSettingBoolean(string value, ref bool target)
	{
		switch (value)
		{
			case "on":
				target = true;
				break;
			case "off":
				target = false;
				break;
			case "toggle":
				target = !target;
				break;
			default:
				Services.Chat.PrintError(
					$"Unknown boolean value '{value}. Valid values are: on,off,toggle.");
				break;
		}
	}

	public void HydrateOverlays()
	{
		// Hydrate any overlays in the config
		foreach (InlayConfiguration? overlayConfig in Config.Inlays)
		{
			if (!overlayConfig.Disabled && (!overlayConfig.ActOptimizations || _actAvailable))
			{
				OverlayAdded?.Invoke(this, overlayConfig);
			}
		}
	}

	private InlayConfiguration? AddNewOverlay()
	{
		InlayConfiguration? overlayConfig = new() { Guid = Guid.NewGuid(), Name = "New overlay", Url = "about:blank" };
		Config.Inlays.Add(overlayConfig);
		OverlayAdded?.Invoke(this, overlayConfig);
		SaveSettings();

		return overlayConfig;
	}

	private void NavigateOverlay(InlayConfiguration overlayConfig)
	{
		if (overlayConfig.Url == "") { overlayConfig.Url = "about:blank"; }

		OverlayNavigated?.Invoke(this, overlayConfig);
	}

	private void UpdateZoomOverlay(InlayConfiguration overlayConfig)
	{
		OverlayZoomed?.Invoke(this, overlayConfig);
	}

	private void UpdateMuteOverlay(InlayConfiguration overlayConfig)
	{
		OverlayMuted?.Invoke(this, overlayConfig);
	}

	private void UpdateUserCss(InlayConfiguration overlayConfig)
	{
		OverlayUserCssChanged?.Invoke(this, overlayConfig);
	}

	private void ReloadOverlay(InlayConfiguration overlayConfig) { NavigateOverlay(overlayConfig); }

	private void DebugOverlay(InlayConfiguration overlayConfig)
	{
		OverlayDebugged?.Invoke(this, overlayConfig);
	}

	private void RemoveOverlay(InlayConfiguration overlayConfig)
	{
		OverlayRemoved?.Invoke(this, overlayConfig);
		Config.Inlays.Remove(overlayConfig);
		SaveSettings();
	}

	private void DebouncedSaveSettings()
	{
		_saveDebounceTimer?.Dispose();
		_saveDebounceTimer = new Timer(_ => SaveSettings(), null, 1000, Timeout.Infinite);
	}

	private void SaveSettings()
	{
		_saveDebounceTimer?.Dispose();
		_saveDebounceTimer = null;
		Services.PluginInterface.SavePluginConfig(Config);
	}

	private string GetOverlayCommandName(InlayConfiguration overlayConfig)
	{
		return Regex.Replace(overlayConfig.Name, @"\s+", "").ToLower();
	}

	public void Render()
	{
		if (!_open) { return; }

		// Primary window container
		ImGui.SetNextWindowSizeConstraints(new Vector2(400, 300), new Vector2(9001, 9001));
		ImGuiWindowFlags windowFlags = ImGuiWindowFlags.None
		                               | ImGuiWindowFlags.NoScrollbar
		                               | ImGuiWindowFlags.NoScrollWithMouse
		                               | ImGuiWindowFlags.NoCollapse;
		ImGui.Begin("Browsingway 设置", ref _open, windowFlags);

		RenderPaneSelector();

		// Pane details
		bool dirty = false;
		ImGui.SameLine();
		ImGui.BeginChild("details");
		if (_selectedOverlay == null)
		{
			dirty |= RenderGeneralSettings();
		}
		else
		{
			dirty |= RenderOverlaySettings(_selectedOverlay);
		}

		ImGui.EndChild();

		if (dirty) { DebouncedSaveSettings(); }

		ImGui.End();
	}

	private void RenderPaneSelector()
	{
		// Selector pane
		ImGui.BeginGroup();
		ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 0));

		int selectorWidth = 100;
		ImGui.BeginChild("panes", new Vector2(selectorWidth, -ImGui.GetFrameHeightWithSpacing()), true);

		// General settings
		if (ImGui.Selectable("通用设置", _selectedOverlay == null))
		{
			_selectedOverlay = null;
		}

		// Overlay selector list
		ImGui.Dummy(new Vector2(0, 5));
		ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
		ImGui.Text("- 叠加层 -");
		ImGui.PopStyleVar();
		foreach (InlayConfiguration? overlayConfig in Config?.Inlays!)
		{
			if (ImGui.Selectable($"{overlayConfig.Name}##{overlayConfig.Guid}", _selectedOverlay == overlayConfig))
			{
				_selectedOverlay = overlayConfig;
			}
		}

		ImGui.EndChild();

		// Selector controls
		ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0);
		ImGui.PushFont(UiBuilder.IconFont);

		int buttonWidth = selectorWidth / 2;
		if (ImGui.Button(FontAwesomeIcon.Plus.ToIconString(), new Vector2(buttonWidth, 0)))
		{
			_selectedOverlay = AddNewOverlay();
		}

		ImGui.SameLine();
		if (_selectedOverlay != null)
		{
			if (ImGui.Button(FontAwesomeIcon.Trash.ToIconString(), new Vector2(buttonWidth, 0)))
			{
				InlayConfiguration? toRemove = _selectedOverlay;
				_selectedOverlay = null;
				RemoveOverlay(toRemove);
			}
		}
		else
		{
			ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
			ImGui.Button(FontAwesomeIcon.Trash.ToIconString(), new Vector2(buttonWidth, 0));
			ImGui.PopStyleVar();
		}

		ImGui.PopFont();
		ImGui.PopStyleVar(2);

		ImGui.EndGroup();
	}

	private bool RenderGeneralSettings()
	{
		bool dirty = false;

		ImGui.Text("请在左侧选择要编辑设置的叠加层");

		if (ImGui.CollapsingHeader("指令帮助", ImGuiTreeNodeFlags.DefaultOpen))
		{
			// TODO: If this ever gets more than a few options, should probably colocate help with the defintion. Attributes?
			ImGui.Text("/bw config");
			ImGui.Text("打开此配置窗口");
			ImGui.Dummy(new Vector2(0, 5));

			ImGui.Text("/bw overlay [叠加层指令名称] [设置项] [值]");
			ImGui.TextWrapped(
				"修改叠加层的设置项\n" +
				"\t叠加层指令名称: 要编辑的叠加层。使用其配置中显示的'指令名称'\n" +
				"\t设置项: 要修改的值。可接受的设置项有:\n" +
				"\t\turl: 字符串\n" +
				"\t\tdisabled: 布尔值\n" +
				"\t\tmuted: 布尔值\n" +
				"\t\tact: 布尔值\n" +
				"\t\tlocked: 布尔值\n" +
				"\t\thidden: 布尔值\n" +
				"\t\ttypethrough: 布尔值\n" +
				"\t\tclickthrough: 布尔值\n" +
				"\t\tfullscreen: 布尔值\n" +
				"\t\treload: -\n" +
				"\t值: 为设置项指定的值。可接受的值有:\n" +
				"\t\t字符串: 任意字符串值\n\t\t布尔值: on, off, toggle");
		}

		return dirty;
	}

	private bool RenderOverlaySettings(InlayConfiguration overlayConfig)
	{
		bool dirty = false;

		ImGui.PushID(overlayConfig.Guid.ToString());

		dirty |= ImGui.InputText("名称", ref overlayConfig.Name, 100);

		ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
		string? commandName = GetOverlayCommandName(overlayConfig);
		ImGui.InputText("指令名称", ref commandName, 100);
		ImGui.PopStyleVar();

		dirty |= ImGui.InputText("URL", ref overlayConfig.Url, 1000);
		if (ImGui.IsItemDeactivatedAfterEdit()) { NavigateOverlay(overlayConfig); }

		if (ImGui.InputFloat("缩放", ref overlayConfig.Zoom, 1f, 10f, "%.0f%%"))
		{
			// clamp to allowed range 
			if (overlayConfig.Zoom < 10f)
			{
				overlayConfig.Zoom = 10f;
			}
			else if (overlayConfig.Zoom > 500f)
			{
				overlayConfig.Zoom = 500f;
			}

			dirty = true;

			// notify of zoom change
			UpdateZoomOverlay(overlayConfig);
		}

		if (ImGui.InputFloat("透明度", ref overlayConfig.Opacity, 1f, 10f, "%.0f%%"))
		{
			// clamp to allowed range 
			if (overlayConfig.Opacity < 10f)
			{
				overlayConfig.Opacity = 10f;
			}
			else if (overlayConfig.Opacity > 100f)
			{
				overlayConfig.Opacity = 100f;
			}

			dirty = true;
		}

		if (ImGui.InputInt("帧率", ref overlayConfig.Framerate, 1, 10))
		{
			// clamp to allowed range 
			if (overlayConfig.Framerate < 1)
			{
				overlayConfig.Framerate = 1;
			}
			else if (overlayConfig.Framerate > 300)
			{
				overlayConfig.Framerate = 300;
			}

			dirty = true;

			// framerate changes require the recreation of the browser instance
			// TODO: this is ugly as heck, fix once proper IPC is up and running
			OverlayRemoved?.Invoke(this, overlayConfig);
			OverlayAdded?.Invoke(this, overlayConfig);
		}

		ImGui.SetNextItemWidth(100);
		ImGui.Columns(2, "boolInlayOptions", false);

		if (ImGui.Checkbox("禁用", ref overlayConfig.Disabled))
		{
			if (overlayConfig.Disabled)
				OverlayRemoved?.Invoke(this, overlayConfig);
			else
				OverlayAdded?.Invoke(this, overlayConfig);
			dirty = true;
		}

		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("完全禁用叠加层。与隐藏不同，此设置会阻止叠加层被创建。"); }

		ImGui.NextColumn();
		ImGui.NextColumn();


		if (ImGui.Checkbox("静音", ref overlayConfig.Muted))
		{
			UpdateMuteOverlay(overlayConfig);
			dirty = true;
		}

		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("启用或禁用音频播放。"); }

		ImGui.NextColumn();

		if (ImGui.Checkbox("ACT/IINACT优化", ref overlayConfig.ActOptimizations))
		{
			if (!overlayConfig.Disabled)
			{
				if (overlayConfig.ActOptimizations)
				{
					if (!_actAvailable)
						OverlayRemoved?.Invoke(this, overlayConfig);
					else
						OverlayAdded?.Invoke(this, overlayConfig);
				}
				else
				{
					OverlayAdded?.Invoke(this, overlayConfig);
				}
			}

			dirty = true;
		}

		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("启用ACT/IINACT特定优化。如果ACT/IINACT未运行，将自动禁用叠加层。\n\n注意：这不会在websocket未报告数据时禁用叠加层。"); }

		ImGui.NextColumn();

		if (overlayConfig.ClickThrough || overlayConfig.Fullscreen) { ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f); }

		bool true_ = true;
		bool implicit_ = overlayConfig.ClickThrough || overlayConfig.Fullscreen;
		dirty |= ImGui.Checkbox("锁定", ref implicit_ ? ref true_ : ref overlayConfig.Locked);
		if (overlayConfig.ClickThrough) { ImGui.PopStyleVar(); }

		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("防止叠加层被调整大小或移动。点击穿透和全屏模式会隐式启用此选项。"); }

		ImGui.NextColumn();

		dirty |= ImGui.Checkbox("隐藏", ref overlayConfig.Hidden);
		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("隐藏叠加层。这不会停止叠加层执行，仅不显示。"); }

		ImGui.NextColumn();

		if (overlayConfig.ClickThrough) { ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f); }

		dirty |= ImGui.Checkbox("键盘穿透", ref overlayConfig.ClickThrough ? ref true_ : ref overlayConfig.TypeThrough);
		if (overlayConfig.ClickThrough || overlayConfig.Fullscreen) { ImGui.PopStyleVar(); }

		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("防止叠加层拦截任何键盘事件。点击穿透会隐式启用此选项。"); }

		ImGui.NextColumn();

		dirty |= ImGui.Checkbox("点击穿透", ref overlayConfig.ClickThrough);
		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("防止叠加层拦截任何鼠标事件。会隐式启用锁定和键盘穿透。"); }

		ImGui.NextColumn();

		dirty |= ImGui.Checkbox("非战斗时隐藏", ref overlayConfig.HideOutOfCombat);
		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("当处于非战斗状态时隐藏此叠加层。"); }

		ImGui.NextColumn();
		dirty |= ImGui.Checkbox("鼠标悬停时显示", ref overlayConfig.ShowOnHover);
		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("当叠加层隐藏时，鼠标悬停在其区域上会临时显示。"); }
		ImGui.NextColumn();

		if (!overlayConfig.HideOutOfCombat) { ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f); }

		dirty |= ImGui.InputInt("隐藏延迟", ref overlayConfig.HideDelay);
		if (ImGui.IsItemHovered()) { ImGui.SetTooltip("非战斗状态下隐藏叠加层的延迟时间(秒)。"); }

		if (!overlayConfig.HideOutOfCombat) { ImGui.PopStyleVar(); }

		ImGui.Columns(1);

		ImGui.NewLine();
		if (ImGui.CollapsingHeader("实验性/未支持功能"))
		{
			ImGui.NewLine();
			dirty |= ImGui.Checkbox("全屏模式", ref overlayConfig.Fullscreen);
			ImGui.NewLine();
			if (ImGui.IsItemHovered()) { ImGui.SetTooltip("启用时自动使此叠加层叠加整个屏幕。"); }

			ImGui.Text("自定义CSS代码:");
			if (ImGui.InputTextMultiline("Custom CSS code", ref overlayConfig.CustomCss, 1000000,
				    new Vector2(-1, ImGui.GetTextLineHeight() * 10)))
			{
				dirty = true;
			}

			if (ImGui.IsItemDeactivatedAfterEdit()) { UpdateUserCss(overlayConfig); }
		}

		ImGui.NewLine();
		if (ImGui.Button("重新加载")) { ReloadOverlay(overlayConfig); }

		ImGui.SameLine();
		if (ImGui.Button("开发者工具")) { DebugOverlay(overlayConfig); }

		ImGui.PopID();

		return dirty;
	}
}