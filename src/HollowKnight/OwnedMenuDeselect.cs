// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections;
namespace YiGodhome;

internal static class OwnedMenuDeselect
{
    internal static IEnumerator Guard(IEnumerator native,Func<bool>reselected)
    {
        try {
            while(native.MoveNext()) {
                yield return native.Current;
                if(reselected())yield break;
            }
        } finally {(native as IDisposable)?.Dispose();}
    }
}
