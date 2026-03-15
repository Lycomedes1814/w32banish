#include <string.h>
#include <X11/Xlib.h>
#include <X11/extensions/Xfixes.h>

int main(int argc, char *argv[])
{
    if (argc < 2) return 1;
    Display *dpy = XOpenDisplay(NULL);
    if (!dpy) return 1;
    Window root = DefaultRootWindow(dpy);
    if (strcmp(argv[1], "hide") == 0)
        XFixesHideCursor(dpy, root);
    else if (strcmp(argv[1], "show") == 0)
        XFixesShowCursor(dpy, root);
    XFlush(dpy);
    XCloseDisplay(dpy);
    return 0;
}
