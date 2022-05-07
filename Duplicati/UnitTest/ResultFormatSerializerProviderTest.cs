using Duplicati.Library.Modules.Builtin;
using Duplicati.Library.Modules.Builtin.ResultSerialization;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class ResultFormatSerializerProviderTest : BasicSetupHelper
    {
        [Test]
        [Category("Serialization")]
        public void TestGetSerializerGivenDuplicatiReturnsDuplicatiSerializer()
        {
            IResultFormatSerializer serializer = ResultFormatSerializerProvider.GetSerializer(ResultExportFormat.Duplicati);
            Assert.AreEqual(typeof(DuplicatiFormatSerializer), serializer.GetType());
        }

        [Test]
        [Category("Serialization")]
        public void TestGetSerializerGivenJsonReturnsJsonSerializer()
        {
            IResultFormatSerializer serializer = ResultFormatSerializerProvider.GetSerializer(ResultExportFormat.Json);
            Assert.AreEqual(typeof(JsonFormatSerializer), serializer.GetType());
        }
    }
}
