/*******************************************************************
*                         Bluberry v0.0.1
*          Created by Ranyodh Singh Mandur - 🫐 2026-2026
*
*               Licensed under the MIT License (MIT).
*          For more details, see the LICENSE file or visit:
*                https://opensource.org/licenses/MIT
*
*            Bluberry is a free open source game engine
********************************************************************/
#include <stdio.h>

#include <physfs/physfs.h>
#include <iso646.h>

///Bluberry
#include "Input.h"



static bool
    BLUB_InitializePhysFS(const char* fp_RootPath)
{
    if (not PHYSFS_init(fp_RootPath))
    {
        printf("Failed to initialize PhysFS: %s\n", PHYSFS_getErrorByCode(PHYSFS_getLastErrorCode()));
        return false;
    }
    // Set the writable directory to the repo root
    else if (not PHYSFS_setWriteDir(fp_RootPath))
    {
        printf("Failed to set write directory: %s\n", PHYSFS_getErrorByCode(PHYSFS_getLastErrorCode()));
        return false;
    }
    // Mount the root directory for asset loading
    else if (not PHYSFS_mount(fp_RootPath, NULL, 1))
    {
        printf("Failed to set search path: %s\n", PHYSFS_getErrorByCode(PHYSFS_getLastErrorCode()));
        return false;
    }

    printf("PhysFS initialized at root: %s\n", fp_RootPath);
    return true;
}

int main(int argc, char** argv)
{
    printf("Hello World >w<! argc: %d\n", argc);

    BLUB_InitializePhysFS(argv[0]);

    SDL_Window* f_MainWindow = SDL_CreateWindow //implicitly calls SDL_Init for the video subsystem owo
    (
        "Hello World!",
        800,
        600,
        SDL_WINDOW_OPENGL | SDL_WINDOW_RESIZABLE
    );

    if (not f_MainWindow)
    {
        printf("Window could not be created! SDL_Error: %s", SDL_GetError());
        return -1;
    }

    bool f_IsRunning = true;

    while(f_IsRunning)
    {
        BLUB_PollEvents(&f_IsRunning);
    }


    return 0;
}
