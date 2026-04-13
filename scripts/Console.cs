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

public partial class Console : Node2D
{
	public static Console Instance;

	public void
		PrintError(string fp_ErrorString)
	{
		
	}

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public override void
        _EnterTree()
    {
        Instance = this;
        this.Visible = false;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public override void
        _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        // CleanUpSceneLoggers();
    }
}
