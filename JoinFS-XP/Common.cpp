#include <stdarg.h>
#include <stdio.h>

#ifdef _WINDOWS
#define IBM 1
#else
#define LIN 1
#endif

#include "XPLMUtilities.h"

#include "Common.h"


void Common::DebugMsg(const char* format,  ... )
{
    static char str[256];
    va_list args;
    va_start(args, format);
    vsnprintf(str, 256, format, args);
    va_end(args);

    // send to log
	XPLMDebugString(productName);
	XPLMDebugString(": ");
	XPLMDebugString(str);
}
