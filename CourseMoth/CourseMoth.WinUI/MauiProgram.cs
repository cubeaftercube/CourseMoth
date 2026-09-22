// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth;
using CourseMoth.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CourseMoth.WinUI
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();

            builder
                .UseSharedMauiApp()
                .UsePlatformServices(services =>
                {
                    // The folder dialog is a Windows API, so it is supplied here rather than in
                    // the shared project, which targets net10.0 and cannot reach one. See
                    // IFolderPicker for why no stand-in is registered there.
                    services.AddSingleton<IFolderPicker, WindowsFolderPicker>();
                });

            return builder.Build();
        }
    }
}
