using System.Reflection;
using System.Reflection.Emit;
using CmsSync.Domain.Events;

namespace CmsSync.UnitTests.TestSupport;

internal static class DynamicValidatedEventFactory
{
    private const string IgnoresAccessChecksAttributeName =
        "System.Runtime.CompilerServices.IgnoresAccessChecksToAttribute";

    public static ValidatedCmsEvent Create(string entityId, UtcTimestamp occurredAtUtc)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("CmsSync.UnitTests.UnsupportedEvents"),
            AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("UnsupportedEvents");
        var ignoresAccessChecksAttribute = DefineIgnoresAccessChecksAttribute(module);
        var attributeConstructor = ignoresAccessChecksAttribute.GetConstructor([typeof(string)])
            ?? throw new InvalidOperationException("The dynamic access-check attribute constructor was not created.");
        assembly.SetCustomAttribute(
            new CustomAttributeBuilder(attributeConstructor, ["CmsSync.Domain"]));

        var type = DefineUnsupportedEvent(module);
        return (ValidatedCmsEvent)(Activator.CreateInstance(type, entityId, occurredAtUtc)
            ?? throw new InvalidOperationException("The unsupported event subtype could not be created."));
    }

    private static Type DefineIgnoresAccessChecksAttribute(ModuleBuilder module)
    {
        var typeBuilder = module.DefineType(
            IgnoresAccessChecksAttributeName,
            TypeAttributes.Public | TypeAttributes.Sealed,
            typeof(Attribute));
        var targetAssembly = typeBuilder.DefineField(
            "_targetAssembly",
            typeof(string),
            FieldAttributes.Private | FieldAttributes.InitOnly);
        var constructor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [typeof(string)]);
        var attributeConstructor = typeof(Attribute).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            Type.EmptyTypes,
            modifiers: null)
            ?? throw new InvalidOperationException("The Attribute constructor is unavailable.");
        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, attributeConstructor);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, targetAssembly);
        il.Emit(OpCodes.Ret);

        return typeBuilder.CreateType();
    }

    private static Type DefineUnsupportedEvent(ModuleBuilder module)
    {
        var typeBuilder = module.DefineType(
            "UnsupportedValidatedCmsEvent",
            TypeAttributes.Public | TypeAttributes.Sealed,
            typeof(ValidatedCmsEvent));
        DefineRecordClone(typeBuilder);
        var constructor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [typeof(string), typeof(UtcTimestamp)]);
        var baseConstructor = typeof(ValidatedCmsEvent).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(string), typeof(UtcTimestamp)],
            modifiers: null)
            ?? throw new InvalidOperationException("The validated event constructor is unavailable.");
        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, baseConstructor);
        il.Emit(OpCodes.Ret);

        return typeBuilder.CreateType();
    }

    private static void DefineRecordClone(TypeBuilder typeBuilder)
    {
        var baseClone = typeof(ValidatedCmsEvent).GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(method => method.IsAbstract && method.Name == "<Clone>$");
        var clone = typeBuilder.DefineMethod(
            baseClone.Name,
            baseClone.Attributes & ~MethodAttributes.Abstract & ~MethodAttributes.NewSlot,
            baseClone.CallingConvention,
            baseClone.ReturnType,
            Type.EmptyTypes);
        var il = clone.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
        typeBuilder.DefineMethodOverride(clone, baseClone);
    }
}
