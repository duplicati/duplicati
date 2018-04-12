using Duplicati.Library.Modules.Builtin.ResultSerialization;
using NUnit.Framework;

namespace Duplicati.Library.Modules.Builtin.UnitTest
{
    [TestFixture]
    public class ResultFormatSerializerProviderTest
    {
        [Test]
        public void TestGetSerializerGivenDuplicatiReturnsDuplicatiSerializer()
        {
            IResultFormatSerializer serializer = ResultFormatSerializerProvider.GetSerializer(ResultExportFormat.Duplicati);
            Assert.AreEqual(typeof(DuplicatiFormatSerializer), serializer.GetType());
        }

        [Test]
        public void TestGetSerializerGivenJsonReturnsJsonSerializer()
        {
            IResultFormatSerializer serializer = ResultFormatSerializerProvider.GetSerializer(ResultExportFormat.Json);
            Assert.AreEqual(typeof(JsonFormatSerializer), serializer.GetType());
        }
    }
}
