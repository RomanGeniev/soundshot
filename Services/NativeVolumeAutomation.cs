using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace GraphiteCompanion.Services;

/// <summary>
/// Native UIA COM client, separate from the managed UiaFind implementation.
/// Create/use/dispose on the caller's background MTA; never on the WPF UI thread.
/// All returned elements and patterns own a COM reference and must be disposed.
/// </summary>
internal sealed class NativeVolumeAutomation : IDisposable
{
    private IAutomation2? client;
    private IWalker? walker;
    internal NativeVolumeAutomation(uint timeoutMs = 1500)
    {
        if (timeoutMs < 100 || timeoutMs > 10000) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
        var type = Type.GetTypeFromCLSID(new Guid("E22AD333-B25F-460C-83D0-0581107395C9"), true)!;
        object instance = Activator.CreateInstance(type)!;
        try
        {
            client = (IAutomation2)instance;
            Check(client.put_AutoSetFocus(0));
            Check(client.put_ConnectionTimeout(timeoutMs));
            Check(client.put_TransactionTimeout(timeoutMs));
            Check(client.get_RawViewWalker(out walker));
        }
        catch { if (walker != null) Release(walker); walker = null; client = null; Release(instance); throw; }
    }
    private IAutomation2 Client => client ?? throw new ObjectDisposedException(nameof(NativeVolumeAutomation));
    internal Element FromHandle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) throw new ArgumentException("A live window handle is required.", nameof(hwnd));
        Check(Client.ElementFromHandle(hwnd, out var result));
        return new Element(result ?? throw new InvalidOperationException("UIA returned no window element."));
    }
    internal Element? FindFirstSlider(Element window, string exactName)
    {
        if (string.IsNullOrWhiteSpace(exactName)) throw new ArgumentException("An exact volume name is required.", nameof(exactName));
        ICondition? name = null, slider = null, both = null;
        try
        {
            Check(Client.CreatePropertyCondition(30005, exactName, out name));
            Check(Client.CreatePropertyCondition(30003, 50015, out slider));
            Check(Client.CreateAndCondition(name, slider, out both));
            Check(window.Native.FindFirst(4, both, out var found)); // TreeScope_Descendants
            return found == null ? null : new Element(found);
        }
        finally { Release(both); Release(slider); Release(name); }
    }
    internal IReadOnlyList<Element> FindSliders(Element window, CancellationToken token = default)
    {
        ICondition? slider = null, range = null, either = null;
        IElementArray? array = null;
        var result = new List<Element>();
        try
        {
            token.ThrowIfCancellationRequested();
            Check(Client.CreatePropertyCondition(30003, 50015, out slider));
            Check(Client.CreatePropertyCondition(30033, true, out range)); // IsRangeValuePatternAvailable
            Check(Client.CreateOrCondition(slider, range, out either));
            Check(window.Native.FindAll(4, either, out array));
            if (array == null) return result;
            Check(array.get_Length(out int count));
            // Do not silently select one control from a truncated ambiguous result.
            if (count < 0 || count > 512) throw new InvalidOperationException("Too many UIA range controls in the player window.");
            for (int i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();
                Check(array.GetElement(i, out var item));
                if (item != null) result.Add(new Element(item));
            }
            return result;
        }
        catch { foreach (var item in result) item.Dispose(); throw; }
        finally { Release(array); Release(either); Release(range); Release(slider); }
    }
    internal bool IsDescendantOf(Element element, Element window, int maximumDepth = 64, CancellationToken token = default)
    {
        if (maximumDepth < 1 || maximumDepth > 256) throw new ArgumentOutOfRangeException(nameof(maximumDepth));
        var automation = Client;
        var tree = walker ?? throw new ObjectDisposedException(nameof(NativeVolumeAutomation));
        IElement current = element.Native;
        bool ownsCurrent = false;
        try
        {
            for (int depth = 0; depth < maximumDepth; depth++)
            {
                token.ThrowIfCancellationRequested();
                Check(automation.CompareElements(current, window.Native, out int same));
                if (same != 0) return true;
                Check(tree.GetParentElement(current, out var parent));
                if (ownsCurrent) Release(current);
                ownsCurrent = false;
                if (parent == null) return false;
                current = parent;
                ownsCurrent = true;
            }
            return false;
        }
        finally { if (ownsCurrent) Release(current); }
    }
    public void Dispose()
    {
        var oldWalker = walker; walker = null; Release(oldWalker);
        var oldClient = client; client = null; Release(oldClient);
    }
    internal sealed class Element : IDisposable
    {
        private IElement? element;
        internal Element(IElement element) { this.element = element; }
        internal IElement Native => element ?? throw new ObjectDisposedException(nameof(Element));
        private object? Property(int id) { Check(Native.GetCurrentPropertyValue(id, out var value)); return value; }
        internal string Name => Property(30005) as string ?? "";
        internal string AutomationId => Property(30011) as string ?? "";
        internal string ProviderDescription => Property(30107) as string ?? "";
        internal int ProcessId => Property(30002) is int value ? value : 0;
        internal bool IsEnabled => Property(30010) is bool value && value;
        internal bool IsOffscreen => Property(30022) is not bool value || value;
        internal bool TryGetRange(out Range? range)
        {
            range = null;
            int hr = Native.GetCurrentPattern(10003, out var pattern);
            if (hr == unchecked((int)0x80040200)) return false; // UIA_E_NOTSUPPORTED
            Check(hr);
            if (pattern == null) return false;
            if (pattern is not IRange nativeRange) { Release(pattern); return false; }
            range = new Range(nativeRange);
            return true;
        }
        public void Dispose() { var old = element; element = null; Release(old); }
    }
    internal readonly record struct RangeState(double Value, double Minimum, double Maximum, bool IsReadOnly, double SmallChange);
    internal sealed class Range : IDisposable
    {
        private IRange? range;
        internal Range(IRange range) { this.range = range; }
        private IRange Native => range ?? throw new ObjectDisposedException(nameof(Range));
        internal RangeState Read()
        {
            var r = Native;
            Check(r.get_CurrentIsReadOnly(out int readOnly));
            Check(r.get_CurrentMinimum(out double minimum));
            Check(r.get_CurrentMaximum(out double maximum));
            Check(r.get_CurrentValue(out double value));
            // Some Chromium providers have no SmallChange, although the range itself is writable.
            int stepHr = r.get_CurrentSmallChange(out double step);
            if (stepHr < 0 || !double.IsFinite(step) || step < 0) step = 0;
            return new RangeState(value, minimum, maximum, readOnly != 0, step);
        }
        internal void SetValue(double value)
        {
            if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            Check(Native.SetValue(value));
        }
        public void Dispose() { var old = range; range = null; Release(old); }
    }
    private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
    private static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }

    // The interfaces below retain SDK vtable order, including deliberately unused slots.
    // Source: microsoft/win32metadata generation/WinSDK/RecompiledIdlHeaders/um/UIAutomationClient.h.
    // Do not remove/reorder placeholders. All called methods preserve HRESULT explicitly.
    [ComImport, Guid("34723aff-0c9d-49d0-9896-7ab52df8cd8a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAutomation2
    {
        [PreserveSig] int CompareElements(IElement first, IElement second, out int same);
        [PreserveSig] int CompareRuntimeIds(); // Unused SDK slot 4
        [PreserveSig] int GetRootElement(); // Unused SDK slot 5
        [PreserveSig] int ElementFromHandle(IntPtr hwnd, out IElement? element);
        [PreserveSig] int ElementFromPoint(); // Unused SDK slot 7
        [PreserveSig] int GetFocusedElement(); // Unused SDK slot 8
        [PreserveSig] int GetRootElementBuildCache(); // Unused SDK slot 9
        [PreserveSig] int ElementFromHandleBuildCache(); // Unused SDK slot 10
        [PreserveSig] int ElementFromPointBuildCache(); // Unused SDK slot 11
        [PreserveSig] int GetFocusedElementBuildCache(); // Unused SDK slot 12
        [PreserveSig] int CreateTreeWalker(); // Unused SDK slot 13
        [PreserveSig] int get_ControlViewWalker(); // Unused SDK slot 14
        [PreserveSig] int get_ContentViewWalker(); // Unused SDK slot 15
        [PreserveSig] int get_RawViewWalker(out IWalker? walker);
        [PreserveSig] int get_RawViewCondition(); // Unused SDK slot 17
        [PreserveSig] int get_ControlViewCondition(); // Unused SDK slot 18
        [PreserveSig] int get_ContentViewCondition(); // Unused SDK slot 19
        [PreserveSig] int CreateCacheRequest(); // Unused SDK slot 20
        [PreserveSig] int CreateTrueCondition(); // Unused SDK slot 21
        [PreserveSig] int CreateFalseCondition(); // Unused SDK slot 22
        [PreserveSig] int CreatePropertyCondition(int propertyId, [MarshalAs(UnmanagedType.Struct)] object value, out ICondition condition);
        [PreserveSig] int CreatePropertyConditionEx(); // Unused SDK slot 24
        [PreserveSig] int CreateAndCondition(ICondition first, ICondition second, out ICondition condition);
        [PreserveSig] int CreateAndConditionFromArray(); // Unused SDK slot 26
        [PreserveSig] int CreateAndConditionFromNativeArray(); // Unused SDK slot 27
        [PreserveSig] int CreateOrCondition(ICondition first, ICondition second, out ICondition condition);
        [PreserveSig] int CreateOrConditionFromArray(); // Unused SDK slot 29
        [PreserveSig] int CreateOrConditionFromNativeArray(); // Unused SDK slot 30
        [PreserveSig] int CreateNotCondition(); // Unused SDK slot 31
        [PreserveSig] int AddAutomationEventHandler(); // Unused SDK slot 32
        [PreserveSig] int RemoveAutomationEventHandler(); // Unused SDK slot 33
        [PreserveSig] int AddPropertyChangedEventHandlerNativeArray(); // Unused SDK slot 34
        [PreserveSig] int AddPropertyChangedEventHandler(); // Unused SDK slot 35
        [PreserveSig] int RemovePropertyChangedEventHandler(); // Unused SDK slot 36
        [PreserveSig] int AddStructureChangedEventHandler(); // Unused SDK slot 37
        [PreserveSig] int RemoveStructureChangedEventHandler(); // Unused SDK slot 38
        [PreserveSig] int AddFocusChangedEventHandler(); // Unused SDK slot 39
        [PreserveSig] int RemoveFocusChangedEventHandler(); // Unused SDK slot 40
        [PreserveSig] int RemoveAllEventHandlers(); // Unused SDK slot 41
        [PreserveSig] int IntNativeArrayToSafeArray(); // Unused SDK slot 42
        [PreserveSig] int IntSafeArrayToNativeArray(); // Unused SDK slot 43
        [PreserveSig] int RectToVariant(); // Unused SDK slot 44
        [PreserveSig] int VariantToRect(); // Unused SDK slot 45
        [PreserveSig] int SafeArrayToRectNativeArray(); // Unused SDK slot 46
        [PreserveSig] int CreateProxyFactoryEntry(); // Unused SDK slot 47
        [PreserveSig] int get_ProxyFactoryMapping(); // Unused SDK slot 48
        [PreserveSig] int GetPropertyProgrammaticName(); // Unused SDK slot 49
        [PreserveSig] int GetPatternProgrammaticName(); // Unused SDK slot 50
        [PreserveSig] int PollForPotentialSupportedPatterns(); // Unused SDK slot 51
        [PreserveSig] int PollForPotentialSupportedProperties(); // Unused SDK slot 52
        [PreserveSig] int CheckNotSupported(); // Unused SDK slot 53
        [PreserveSig] int get_ReservedNotSupportedValue(); // Unused SDK slot 54
        [PreserveSig] int get_ReservedMixedAttributeValue(); // Unused SDK slot 55
        [PreserveSig] int ElementFromIAccessible(); // Unused SDK slot 56
        [PreserveSig] int ElementFromIAccessibleBuildCache(); // Unused SDK slot 57
        [PreserveSig] int get_AutoSetFocus(out int value);
        [PreserveSig] int put_AutoSetFocus(int value);
        [PreserveSig] int get_ConnectionTimeout(out uint value);
        [PreserveSig] int put_ConnectionTimeout(uint value);
        [PreserveSig] int get_TransactionTimeout(out uint value);
        [PreserveSig] int put_TransactionTimeout(uint value);
    }
    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IElement
    {
        [PreserveSig] int SetFocus(); // Unused SDK slot 3
        [PreserveSig] int GetRuntimeId(); // Unused SDK slot 4
        [PreserveSig] int FindFirst(int scope, ICondition condition, out IElement? element);
        [PreserveSig] int FindAll(int scope, ICondition condition, out IElementArray? elements);
        [PreserveSig] int FindFirstBuildCache(); // Unused SDK slot 7
        [PreserveSig] int FindAllBuildCache(); // Unused SDK slot 8
        [PreserveSig] int BuildUpdatedCache(); // Unused SDK slot 9
        [PreserveSig] int GetCurrentPropertyValue(int propertyId, [MarshalAs(UnmanagedType.Struct)] out object? value);
        [PreserveSig] int GetCurrentPropertyValueEx(); // Unused SDK slot 11
        [PreserveSig] int GetCachedPropertyValue(); // Unused SDK slot 12
        [PreserveSig] int GetCachedPropertyValueEx(); // Unused SDK slot 13
        [PreserveSig] int GetCurrentPatternAs(); // Unused SDK slot 14
        [PreserveSig] int GetCachedPatternAs(); // Unused SDK slot 15
        [PreserveSig] int GetCurrentPattern(int patternId, [MarshalAs(UnmanagedType.IUnknown)] out object? pattern);
    }
    [ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IElementArray
    {
        [PreserveSig] int get_Length(out int length);
        [PreserveSig] int GetElement(int index, out IElement? element);
    }
    [ComImport, Guid("4042c624-389c-4afc-a630-9df854a541fc"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IWalker
    {
        [PreserveSig] int GetParentElement(IElement element, out IElement? parent);
    }
    [ComImport, Guid("59213f4f-7346-49e5-b120-80555987a148"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IRange
    {
        [PreserveSig] int SetValue(double value);
        [PreserveSig] int get_CurrentValue(out double value);
        [PreserveSig] int get_CurrentIsReadOnly(out int value);
        [PreserveSig] int get_CurrentMaximum(out double value);
        [PreserveSig] int get_CurrentMinimum(out double value);
        [PreserveSig] int get_CurrentLargeChange(out double value);
        [PreserveSig] int get_CurrentSmallChange(out double value);
        [PreserveSig] int get_CachedValue(out double value);
        [PreserveSig] int get_CachedIsReadOnly(out int value);
        [PreserveSig] int get_CachedMaximum(out double value);
        [PreserveSig] int get_CachedMinimum(out double value);
        [PreserveSig] int get_CachedLargeChange(out double value);
        [PreserveSig] int get_CachedSmallChange(out double value);
    }
    [ComImport, Guid("352ffba8-0973-437c-a61f-f64cafd81df9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICondition { }
}

