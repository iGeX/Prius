namespace Prius.Engine.Abstractions;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public record DataTransaction(ISystemElementContext Context, IReadOnlyList<IIntent> Intents);

public interface IDataIntentsProvider
{
    ValueTask<DataTransaction> PopTx(CancellationToken ct);
}


