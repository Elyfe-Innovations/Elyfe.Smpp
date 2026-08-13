/************************************************************************
 * Copyright (C) 2007 Jamaa Technologies
 *
 * This file is part of Jamaa SMPP Library.
 *
 * Jamaa SMPP Library is free software. You can redistribute it and/or modify
 * it under the terms of the Microsoft Reciprocal License (Ms-RL)
 *
 * You should have received a copy of the Microsoft Reciprocal License
 * along with Jamaa SMPP Library; See License.txt for more details.
 *
 * Author: Benedict J. Tesha
 * benedict.tesha@jamaatech.com, www.jamaatech.com
 *
 ************************************************************************/

using System.Threading.Channels;
using System.Threading.Tasks;
using JamaaTech.Smpp.Net.Lib.Protocol;

namespace JamaaTech.Smpp.Net.Lib.Util;

/// <summary>
/// Processes PDUs off a bounded queue on the component's work loop.
/// </summary>
public abstract class PduProcessor<T> : RunningComponent where T : PDU
{
  #region Variables

  private readonly int vQueueCapacity;
  private Channel<T> vChannel;

  #endregion

  #region Constants

  private const int DEFAULT_CAPACITY = 256;

  #endregion

  #region Constructors

  public PduProcessor()
    : this(DEFAULT_CAPACITY)
  {
  }

  public PduProcessor(int defaultQueueCapacity)
  {
    if (defaultQueueCapacity <= 0)
      throw new ArgumentOutOfRangeException(nameof(defaultQueueCapacity), defaultQueueCapacity,
        "Queue capacity must be greater than zero.");

    vQueueCapacity = defaultQueueCapacity;
  }

  #endregion

  #region Methods

  #region Interface Methods

  protected abstract void PostProcessPdu(T pdu);

  /// <summary>
  /// Queues <paramref name="pdu"/> for processing, blocking the caller while the queue is
  /// full so that a fast producer cannot outrun <see cref="PostProcessPdu"/>.
  /// </summary>
  internal void ProcessPdu(T pdu)
  {
    // The work loop owns a dedicated thread, and so does whoever feeds it; blocking the
    // producer here is the backpressure.
    ProcessPduAsync(pdu).AsTask().GetAwaiter().GetResult();
  }

  /// <inheritdoc cref="ProcessPdu"/>
  internal async ValueTask ProcessPduAsync(T pdu, CancellationToken cancellationToken = default)
  {
    var channel = vChannel;
    if (channel == null || !Running) return;

    if (!cancellationToken.CanBeCanceled)
    {
      await channel.Writer.WriteAsync(pdu, StopToken).ConfigureAwait(false);
      return;
    }

    using var linked = CancellationTokenSource.CreateLinkedTokenSource(StopToken, cancellationToken);
    await channel.Writer.WriteAsync(pdu, linked.Token).ConfigureAwait(false);
  }

  protected override void InitializeComponent()
  {
    // A channel cannot be un-completed, so each Start() gets a fresh one.
    vChannel = Channel.CreateBounded<T>(new BoundedChannelOptions(vQueueCapacity)
    {
      SingleReader = true,
      FullMode = BoundedChannelFullMode.Wait
    });

    base.InitializeComponent();
  }

  protected override void RunNow()
  {
    var channel = vChannel;
    if (channel == null) return;

    // RunNow() already owns a dedicated long-running thread; blocking it on the reader
    // is what that thread is for.
    ReadAllAsync(channel.Reader, StopToken).GetAwaiter().GetResult();
  }

  #endregion

  #region Helper Methods

  private async Task ReadAllAsync(ChannelReader<T> reader, CancellationToken cancellationToken)
  {
    try
    {
      await foreach (var pdu in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
      {
        PostProcessPdu(pdu);
      }
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      // Stopped, not an error.
    }
  }

  #endregion

  #endregion
}
