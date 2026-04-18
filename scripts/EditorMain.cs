/*******************************************************************
 *                        Bluberry v0.0.1
 *         Created by Ranyodh Singh Mandur - 🫐 2026-2026
 *
 *              Licensed under the MIT License (MIT).
 *         For more details, see the LICENSE file or visit:
 *               https://opensource.org/licenses/MIT
 *
 *           Bluberry is a free open source game engine
********************************************************************/
using Godot;
using System;

namespace Blueberry;

public partial class EditorMain : Node
{
	TextureButton pm_ExitButton;
	TextureButton pm_MinimizeButton;
	TextureButton pm_ToggleWindowModeButton;


	// Called when the node enters the scene tree for the first time.
	public override void 
		_Ready()
	{
		pm_MinimizeButton = GetNode<TextureButton>("");
		pm_ToggleWindowModeButton = GetNode<TextureButton>("");
		pm_ExitButton = GetNode<TextureButton>("");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
