using System.Text;
using Notey.App.Imports;

namespace Notey.Tests;

public sealed class MsgReaderMessageImportReaderTests
{
    [Fact]
    public async Task ReadAsync_registers_legacy_code_pages_before_creating_storage_message()
    {
        var reader = new MsgReaderMessageImportReader(static _ =>
        {
            var encoding = Encoding.GetEncoding("windows-1252");
            Assert.Equal(1252, encoding.CodePage);
            throw new InvalidOperationException("message-factory-invoked");
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await reader.ReadAsync(
                ImportFile.FromBytes("message.msg", [1]),
                TestContext.Current.CancellationToken));

        Assert.Equal("message-factory-invoked", exception.Message);
    }
}
