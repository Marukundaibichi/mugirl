using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mugirl.SaveUpgrade;
using Verse;

namespace Mugirl.Tests.SaveUpgrade;

public sealed class ReviewedSaveEngineTests
{
    [Fact]
    public void UnreviewedEngineAndUnrelatedMethodsCannotAuthorizeAReader()
    {
        Guid unknown = new("00000000-0000-0000-0000-000000004980");
        Assert.False(ReviewedSaveEngine.IsReviewed(unknown));
        Assert.Equal(0, ReviewedSaveEngine.CanonicalReaderToken(unknown, 0x06002721));
        Assert.Equal(0, ReviewedSaveEngine.CanonicalReaderToken(typeof(Scribe_Collections).Module.ModuleVersionId,
            typeof(Scribe_Collections).GetMethod("BuildDictionary", BindingFlags.Static | BindingFlags.NonPublic)!.MetadataToken));
    }

    [Fact]
    public void ActualDictionaryAndListOverloadsBindToTheirReviewedContracts()
    {
        Module module = typeof(Scribe_Collections).Module;
        Assert.True(ReviewedSaveEngine.IsReviewed(module.ModuleVersionId));
        MethodInfo[] methods = [.. typeof(Scribe_Collections).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.Name == "Look" && m.GetParameters()[0].ParameterType.IsByRef)];
        foreach (MethodInfo method in methods)
        {
            ParameterInfo[] parameters = method.GetParameters();
            Type collection = parameters[0].ParameterType.GetElementType()!.GetGenericTypeDefinition();
            if (collection != typeof(Dictionary<,>) && collection != typeof(List<>)) { continue; }
            int expected = collection == typeof(Dictionary<,>) ? (parameters.Length == 4 ? 0x06002720 : 0x06002722)
                : parameters[1].ParameterType == typeof(bool) ? 0x0600271F
                : parameters[2].ParameterType == typeof(bool) ? 0x0600271C : 0x0600271D;
            Assert.Equal(expected, ReviewedSaveEngine.CanonicalReaderToken(module.ModuleVersionId, method.MetadataToken));
        }
        MethodInfo bank = typeof(LoadIDsWantedBank).GetMethod("RegisterLoadIDListReadFromXml",
            [typeof(List<string>), typeof(string), typeof(IExposable)])!;
        Assert.Equal(0x060026DD, ReviewedSaveEngine.CanonicalReaderToken(module.ModuleVersionId, bank.MetadataToken));
        bank = typeof(LoadIDsWantedBank).GetMethod("RegisterLoadIDListReadFromXml", [typeof(List<string>), typeof(string)])!;
        Assert.Equal(0x060026DE, ReviewedSaveEngine.CanonicalReaderToken(module.ModuleVersionId, bank.MetadataToken));
    }
}
