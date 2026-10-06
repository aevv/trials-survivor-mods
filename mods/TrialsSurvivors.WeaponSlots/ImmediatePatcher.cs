using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using Iced.Intel;
using Il2CppInterop.Common;

namespace TrialsSurvivors.WeaponSlots;

internal readonly record struct ImmediateSite(Register Register, int Vanilla, int Patched, int Expected);

internal sealed record PatchTarget(string Name, MethodBase? Method, params ImmediateSite[] Sites);

internal static unsafe class ImmediatePatcher
{
    private const uint PageExecuteReadWrite = 0x40;

    private readonly record struct Write(string Target, IntPtr Address, byte Vanilla, byte Patched, string Instruction);

    public static bool TryApply(IReadOnlyList<PatchTarget> targets, ManualLogSource log)
    {
        var writes = new List<Write>();
        foreach (var target in targets)
        {
            if (FindWrites(target, writes) is { } failure)
            {
                log.LogError($"{target.Name}: {failure}");
                log.LogError("no native patches applied; the game keeps its vanilla weapon slot count");
                return false;
            }
        }

        var module = GameAssemblyBase();
        foreach (var write in writes)
        {
            WriteByte(write.Address, write.Patched);
            log.LogInfo($"patched {write.Target}: {write.Instruction} -> {write.Patched} at GameAssembly+0x{(long)write.Address - (long)module:X}");
        }

        return true;
    }

    private static string? FindWrites(PatchTarget target, List<Write> writes)
    {
        if (target.Method == null) return "method not found in the interop assembly";

        var start = NativeCodeStart(target.Method);
        if (start == IntPtr.Zero) return "no native code pointer";

        var function = RtlLookupFunctionEntry((ulong)start, out var imageBase, IntPtr.Zero);
        if (function == IntPtr.Zero) return $"no unwind entry for 0x{(long)start:X}, can't size the function";

        var entry = (RuntimeFunction*)function;
        var begin = imageBase + entry->BeginAddress;
        if (begin != (ulong)start) return $"code pointer 0x{(long)start:X} isn't a function start (unwind entry begins at 0x{begin:X}); is something else hooking it?";
        var end = FunctionEnd(begin, imageBase + entry->EndAddress, imageBase);

        var bytes = new byte[end - begin];
        Marshal.Copy(start, bytes, 0, bytes.Length);

        var decoder = Decoder.Create(64, new ByteArrayCodeReader(bytes), begin);
        var instructions = new List<(Instruction Instruction, ConstantOffsets Offsets)>();
        while (decoder.IP < end)
        {
            decoder.Decode(out var instruction);
            if (instruction.IsInvalid) return $"undecodable instruction at 0x{instruction.IP:X}";
            instructions.Add((instruction, decoder.GetConstantOffsets(instruction)));
        }

        var found = new List<Write>();
        foreach (var site in target.Sites)
        {
            var matches = instructions.Where(i => IsCompare(i.Instruction, site.Register, site.Vanilla)).ToList();
            if (matches.Count != site.Expected)
            {
                var alreadyPatched = instructions.Count(i => IsCompare(i.Instruction, site.Register, site.Patched));
                return $"expected {site.Expected}x 'cmp {site.Register.ToString().ToLowerInvariant()},{site.Vanilla}', found {matches.Count}" +
                       (alreadyPatched > 0 ? $" (and {alreadyPatched} already comparing against {site.Patched})" : "");
            }

            foreach (var (instruction, offsets) in matches)
            {
                if (offsets.ImmediateSize != 1) return $"'{instruction}' has a {offsets.ImmediateSize}-byte immediate, expected 1";
                var address = (IntPtr)(long)(instruction.IP + offsets.ImmediateOffset);
                found.Add(new Write(target.Name, address, (byte)site.Vanilla, (byte)site.Patched, instruction.ToString()));
            }
        }

        writes.AddRange(found);
        return null;
    }

    private static ulong FunctionEnd(ulong begin, ulong end, ulong imageBase)
    {
        while (true)
        {
            var next = (RuntimeFunction*)RtlLookupFunctionEntry(end, out var nextBase, IntPtr.Zero);
            if (next == null || nextBase != imageBase || imageBase + next->BeginAddress != end) return end;
            if (ChainRoot(next, imageBase) != begin) return end;
            end = imageBase + next->EndAddress;
        }
    }

    private static ulong ChainRoot(RuntimeFunction* entry, ulong imageBase)
    {
        const int ChainInfo = 0x4;
        var current = entry;
        while (true)
        {
            var unwind = (byte*)(imageBase + current->UnwindData);
            if (((unwind[0] >> 3) & ChainInfo) == 0) return current == entry ? 0 : imageBase + current->BeginAddress;
            var codes = unwind[2];
            current = (RuntimeFunction*)(unwind + 4 + 2 * ((codes + 1) & ~1));
        }
    }

    private static bool IsCompare(in Instruction instruction, Register register, int value) =>
        instruction.Mnemonic == Mnemonic.Cmp &&
        instruction.Op0Kind == OpKind.Register && instruction.Op0Register == register &&
        instruction.Op1Kind is OpKind.Immediate8to32 or OpKind.Immediate8to64 or OpKind.Immediate32 &&
        instruction.Immediate32 == (uint)value;

    private static IntPtr NativeCodeStart(MethodBase method)
    {
        var field = Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(method);
        if (field?.GetValue(null) is not IntPtr methodInfo || methodInfo == IntPtr.Zero) return IntPtr.Zero;
        return *(IntPtr*)methodInfo;
    }

    private static void WriteByte(IntPtr address, byte value)
    {
        if (!VirtualProtect(address, (UIntPtr)1, PageExecuteReadWrite, out var previous))
            throw new InvalidOperationException($"VirtualProtect failed at 0x{(long)address:X} (error {Marshal.GetLastWin32Error()})");

        *(byte*)address = value;
        VirtualProtect(address, (UIntPtr)1, previous, out _);
        FlushInstructionCache(GetCurrentProcess(), address, (UIntPtr)1);
    }

    private static IntPtr GameAssemblyBase() => GetModuleHandle("GameAssembly.dll");

    [StructLayout(LayoutKind.Sequential)]
    private struct RuntimeFunction
    {
        public uint BeginAddress;
        public uint EndAddress;
        public uint UnwindData;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint newProtect, out uint oldProtect);

    [DllImport("kernel32.dll")]
    private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string name);

    [DllImport("ntdll.dll")]
    private static extern IntPtr RtlLookupFunctionEntry(ulong controlPc, out ulong imageBase, IntPtr historyTable);
}
