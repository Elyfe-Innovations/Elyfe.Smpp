/************************************************************************
 * (c) Jamaa Technologies - Interface introduced for pluggable response handling
 ************************************************************************/
using System.Threading.Tasks;
using JamaaTech.Smpp.Net.Lib.Protocol;

namespace JamaaTech.Smpp.Net.Lib
{
    /// <summary>
    /// Abstraction over the handling of SMPP request/response correlation.
    /// </summary>
    public interface IResponseHandler
    {
        int DefaultResponseTimeout { get; }
        int Count { get; }

        void Handle(ResponsePDU pdu);
        ResponsePDU WaitResponse(RequestPDU pdu);
        ResponsePDU WaitResponse(RequestPDU pdu, int timeOut);
        Task<ResponsePDU> WaitResponseAsync(RequestPDU pdu, CancellationToken cancellationToken = default);
        Task<ResponsePDU> WaitResponseAsync(RequestPDU pdu, int timeOut, CancellationToken cancellationToken = default);
    }
}