using System;
using System.Runtime.InteropServices;

namespace Nyvorn.Source.Engine.Input
{
    public static class ClipboardService
    {
        private const string SdlLibrary = "SDL2";
        private static string fallbackText = string.Empty;

        public static bool TrySetText(string text)
        {
            fallbackText = text ?? string.Empty;
            try
            {
                return SDL_SetClipboardText(fallbackText) == 0;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (BadImageFormatException)
            {
                return false;
            }
        }

        public static bool TryGetText(out string text)
        {
            try
            {
                IntPtr pointer = SDL_GetClipboardText();
                if (pointer == IntPtr.Zero)
                {
                    text = fallbackText;
                    return false;
                }

                try
                {
                    text = Marshal.PtrToStringUTF8(pointer) ?? string.Empty;
                    fallbackText = text;
                    return true;
                }
                finally
                {
                    SDL_free(pointer);
                }
            }
            catch (DllNotFoundException)
            {
                text = fallbackText;
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                text = fallbackText;
                return false;
            }
            catch (BadImageFormatException)
            {
                text = fallbackText;
                return false;
            }
        }

        [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern int SDL_SetClipboardText([MarshalAs(UnmanagedType.LPUTF8Str)] string text);

        [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SDL_GetClipboardText();

        [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_free(IntPtr memory);
    }
}
