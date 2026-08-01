using System.Reflection;
using System.Reflection.Emit;
using CmsSync.Domain.Entities;
using CmsSync.Domain.Events;
using CmsSync.Domain.Processing;
using CmsSync.UnitTests.TestSupport;
using Xunit;

namespace CmsSync.UnitTests.Processing;

public sealed class StateMachineInvariantTests
{
    private const string ConfidentialPayload = "confidential-payload-sentinel";

    [Fact]
    public void ActiveEntityIdMismatchFailsFastWithoutAStateDecision()
    {
        var active = CmsStateTestData.Active(entityId: "other", payload: ConfidentialPayload);

        AssertSafeInternalFailure(
            () => CmsEntityStateMachine.Decide(CmsStateTestData.VersionedEvent(), active));
    }

    [Fact]
    public void TombstoneEntityIdMismatchFailsFastWithoutAStateDecision()
    {
        var tombstone = CmsStateTestData.Tombstone(entityId: "other");

        AssertSafeInternalFailure(
            () => CmsEntityStateMachine.Decide(
                CmsStateTestData.VersionedEvent(payload: ConfidentialPayload),
                tombstone: tombstone));
    }

    [Fact]
    public void RevisionEntityIdMismatchFailsFastWithoutAStateDecision()
    {
        var active = CmsStateTestData.Active(payload: ConfidentialPayload);
        var revision = CmsStateTestData.Revision(entityId: "other", payload: ConfidentialPayload);

        AssertSafeInternalFailure(
            () => CmsEntityStateMachine.Decide(
                CmsStateTestData.VersionedEvent(payload: ConfidentialPayload),
                active,
                sameVersionRevision: revision));
    }

    [Fact]
    public void RevisionWithoutActiveEntityFailsFastWithoutAStateDecision()
    {
        var revision = CmsStateTestData.Revision(payload: ConfidentialPayload);

        AssertSafeInternalFailure(
            () => CmsEntityStateMachine.Decide(
                CmsStateTestData.VersionedEvent(payload: ConfidentialPayload),
                sameVersionRevision: revision));
    }

    [Fact]
    public void ActiveGenerationAboveOneWithoutTombstoneFailsFastWithoutAStateDecision()
    {
        var active = CmsStateTestData.Active(generation: 2, payload: ConfidentialPayload);

        AssertSafeInternalFailure(
            () => CmsEntityStateMachine.Decide(
                CmsStateTestData.VersionedEvent(payload: ConfidentialPayload),
                active));
    }

    [Fact]
    public void ActiveAndTombstoneGenerationMismatchFailsFastWithoutAStateDecision()
    {
        var active = CmsStateTestData.Active(generation: 3, payload: ConfidentialPayload);
        var tombstone = CmsStateTestData.Tombstone(lastDeletedGeneration: 1);

        AssertSafeInternalFailure(
            () => CmsEntityStateMachine.Decide(
                CmsStateTestData.VersionedEvent(payload: ConfidentialPayload),
                active,
                tombstone));
    }

    [Theory]
    [InlineData(8, 8, 8)]
    [InlineData(9, 9, 10)]
    public void ActiveTimestampsAtOrBeforeRetainedTombstoneFailFast(
        int currentHour,
        int watermarkHour,
        int tombstoneHour)
    {
        var active = CmsStateTestData.Active(
            generation: 2,
            currentVersionOccurredAtUtc: CmsStateTestData.At(currentHour),
            entityEventHighWatermarkUtc: CmsStateTestData.At(watermarkHour),
            payload: ConfidentialPayload);
        var tombstone = CmsStateTestData.Tombstone(
            lastDeletedGeneration: 1,
            deletedAtUtc: CmsStateTestData.At(tombstoneHour));

        AssertSafeInternalFailure(
            () => CmsEntityStateMachine.Decide(
                CmsStateTestData.VersionedEvent(
                    occurredAtUtc: CmsStateTestData.At(11),
                    payload: ConfidentialPayload),
                active,
                tombstone));
    }

    [Fact]
    public void RevisionGenerationMismatchFailsFastWithoutAStateDecision()
    {
        var active = CmsStateTestData.Active(generation: 2, payload: ConfidentialPayload);
        var tombstone = CmsStateTestData.Tombstone(lastDeletedGeneration: 1);
        var revision = CmsStateTestData.Revision(generation: 1, payload: ConfidentialPayload);

        AssertSafeInternalFailure(
            () => CmsEntityStateMachine.Decide(
                CmsStateTestData.VersionedEvent(payload: ConfidentialPayload),
                active,
                tombstone,
                revision));
    }

    [Fact]
    public void RevisionVersionMismatchFailsFastWithoutAStateDecision()
    {
        var active = CmsStateTestData.Active(payload: ConfidentialPayload);
        var revision = CmsStateTestData.Revision(version: 4, payload: ConfidentialPayload);

        AssertSafeInternalFailure(
            () => CmsEntityStateMachine.Decide(
                CmsStateTestData.VersionedEvent(payload: ConfidentialPayload),
                active,
                sameVersionRevision: revision));
    }

    [Fact]
    public void RevisionPayloadHashMismatchFailsFastWithoutAStateDecision()
    {
        var active = CmsStateTestData.Active(payload: ConfidentialPayload);
        var revision = CmsStateTestData.Revision(
            payload: ConfidentialPayload,
            payloadHash: CmsStateTestData.Hash(2));

        AssertSafeInternalFailure(
            () => CmsEntityStateMachine.Decide(
                CmsStateTestData.VersionedEvent(payload: ConfidentialPayload),
                active,
                sameVersionRevision: revision));
    }

    [Fact]
    public void SameVersionWithoutRequiredRevisionFailsFastWithoutAStateDecision()
    {
        var active = CmsStateTestData.Active(payload: ConfidentialPayload);

        AssertSafeInternalFailure(
            () => CmsEntityStateMachine.Decide(
                CmsStateTestData.VersionedEvent(payload: ConfidentialPayload),
                active));
    }

    [Fact]
    public void UnsupportedValidatedEventSubtypeFailsFastWithoutAStateDecision()
    {
        var unsupported = DynamicValidatedEventFactory.Create(
            CmsStateTestData.EntityId,
            CmsStateTestData.At(10));

        AssertSafeInternalFailure(() => CmsEntityStateMachine.Decide(unsupported));
    }

    private static void AssertSafeInternalFailure(Action action)
    {
        var exception = Assert.Throws<InvalidOperationException>(action);

        Assert.DoesNotContain(ConfidentialPayload, exception.Message, StringComparison.Ordinal);
    }

    private static class DynamicValidatedEventFactory
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
}
