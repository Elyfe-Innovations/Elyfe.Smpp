/************************************************************************
 * Copyright (C) 2008 Jamaa Technologies
 *
 * This file is part of Jamaa SMPP Client Library.
 *
 * Jamaa SMPP Client Library is free software. You can redistribute it and/or modify
 * it under the terms of the Microsoft Reciprocal License (Ms-RL)
 *
 * You should have received a copy of the Microsoft Reciprocal License
 * along with Jamaa SMPP Client Library; See License.txt for more details.
 *
 * Author: Benedict J. Tesha
 * benedict.tesha@jamaatech.com, www.jamaatech.com
 *
 ************************************************************************/

using System;
using System.Collections.Generic;
using JamaaTech.Smpp.Net.Lib.Protocol;
using JamaaTech.Smpp.Net.Lib;

namespace JamaaTech.Smpp.Net.Client
{
    public class TextMessage : ShortMessage
    {
        #region Variables
        private string vText;
        private int vMaxMessageLength;
        #endregion

        #region Constuctors
        /// <summary>
        /// Initializes a new instance of <see cref="TextMessage"/>
        /// </summary>
        public TextMessage()
            : base()
        {
            vText = "";
        }

        internal TextMessage(int segmentId, int messageCount, int sequenceNumber)
            : base(segmentId, messageCount, sequenceNumber)
        {
            vText = "";
        }
        #endregion

        #region Properties
        /// <summary>
        /// Gets or sets a <see cref="System.String"/> value representing the text content of the message
        /// </summary>
        public string Text
        {
            get { return vText; }
            set { vText = value; }
        }

        public int MaxMessageLength { get { return vMaxMessageLength; } }
        #endregion

        #region Methods
        protected override IEnumerable<SendSmPDU> GetPDUs(DataCoding defaultEncoding, SmppEncodingService smppEncodingService, SmppAddress destAddress = null, SmppAddress srcAddress = null)
        {
            destAddress = destAddress ?? new SmppAddress() {Address = vDestinatinoAddress};
            srcAddress = srcAddress ?? new SmppAddress() { Address = vSourceAddress };
            SubmitSm sm = CreateSubmitSm(smppEncodingService, destAddress, srcAddress );
            sm.DataCoding = defaultEncoding;

            if (SubmitUserMessageReference)
                sm.SetOptionalParamString(Lib.Protocol.Tlv.Tag.user_message_reference, UserMessageReference);

            if (SubmitReceiptedMessageId)
                sm.SetOptionalParamString(Lib.Protocol.Tlv.Tag.receipted_message_id, ReceiptedMessageId);

            if (vRegisterDeliveryNotification)
                sm.RegisteredDelivery = RegisteredDelivery.DeliveryReceipt;

            vMaxMessageLength = GetMaxMessageLength(defaultEncoding, false);
            var byteBudget = defaultEncoding == DataCoding.UCS2 ? vMaxMessageLength * 2 : vMaxMessageLength;
            byte[] bytes = smppEncodingService.GetBytesFromString(vText, defaultEncoding);

            // SMSCDefault bytes are unpacked septets, including GSM extension escapes.
            // UCS2 limits are bytes as well, rather than UTF-16 character counts.
            if (bytes.Length > byteBudget)
            {
                var SegID = SegmentIdGeneratorFactory.Generator.NextSegmentId(vSourceAddress, vDestinatinoAddress);
                vMaxMessageLength = GetMaxMessageLength(defaultEncoding, true);
                byteBudget = defaultEncoding == DataCoding.UCS2 ? vMaxMessageLength * 2 : vMaxMessageLength;
                var messages = Split(bytes, byteBudget, defaultEncoding, smppEncodingService);
                var totalSegments = messages.Count; // get the number of (how many) parts
                var udh = new Udh(SegID, totalSegments, 0); // ID, Total, part

                var offset = 0;
                for (int i = 0; i < totalSegments; i++)
                {
                    udh.MessageSequence = i + 1;  // seq+1 , - parts of the message      
                    var page = smppEncodingService.GetStringFromBytes(bytes, offset, messages[i], defaultEncoding);
                    offset += messages[i];
                    // Keep the virtual hook: multipart SubmitSm implementations use
                    // it to select the last page as the response-bearing PDU.
                    sm.SetMessageText(page, defaultEncoding, udh);
                    yield return sm;
                }
            }
            else
            {
                sm.SetMessageText(vText, defaultEncoding);
                yield return sm;
            }
        }

        protected virtual SubmitSm CreateSubmitSm(SmppEncodingService smppEncodingService, SmppAddress destAddress = null, SmppAddress srcAddress = null)
        {
            var sm = new SubmitSm(smppEncodingService, destAddress, srcAddress);

            return sm;
        }

        private static List<int> Split(byte[] message, int maxPartLength, DataCoding encoding, SmppEncodingService encodingService)
        {
            var result = new List<int>();
            var littleEndian = encodingService.UCS2Encoding.CodePage == 1200;
            var utf16 = encodingService.UCS2Encoding.CodePage == 1200 ||
                encodingService.UCS2Encoding.CodePage == 1201;
            var decoder = encoding == DataCoding.UCS2 && !utf16
                ? encodingService.UCS2Encoding.GetDecoder() : null;
            var characters = decoder == null ? null :
                new char[encodingService.UCS2Encoding.GetMaxCharCount(1)];

            for (int offset = 0; offset < message.Length;)
            {
                var length = Math.Min(maxPartLength, message.Length - offset);
                if (offset + length < message.Length)
                {
                    if (encoding == DataCoding.SMSCDefault && message[offset + length - 1] == 0x1B)
                        length--;
                    else if (encoding == DataCoding.UCS2 && utf16)
                    {
                        // A supplementary character must remain on one page, for either
                        // configured UTF-16 byte order.
                        var end = offset + length;
                        var lastCodeUnit = littleEndian
                            ? message[end - 2] | (message[end - 1] << 8)
                            : (message[end - 2] << 8) | message[end - 1];
                        if (lastCodeUnit >= 0xD800 && lastCodeUnit <= 0xDBFF)
                            length -= 2;
                    }
                    else if (decoder != null)
                    {
                        // A decoder can consume an incomplete trailing character without
                        // emitting it. Only an emitted complete character marks a boundary.
                        decoder.Reset();
                        var boundary = 0;
                        for (var i = 0; i < length; i++)
                        {
                            var count = decoder.GetChars(message, offset + i, 1, characters, 0, false);
                            if (count > 0 && !char.IsHighSurrogate(characters[count - 1]))
                                boundary = i + 1;
                        }
                        if (boundary == 0)
                            throw new InvalidOperationException("A character exceeds the text message page budget");
                        length = boundary;
                    }
                }
                result.Add(length);
                offset += length;
            }

            return result;
        }

        private static int GetMaxMessageLength(DataCoding encoding, bool includeUdh)
        {
            switch (encoding)
            {
                case DataCoding.SMSCDefault:
                    return includeUdh ? 153 : 160;
                case DataCoding.Latin1:
                    return includeUdh ? 134 : 140;
                case DataCoding.ASCII:
                    return includeUdh ? 153 : 160;
                case DataCoding.UCS2:
                    return includeUdh ? 67 : 70;
                default:
                    throw new InvalidOperationException("Invalid or unsuported encoding for text message ");
            }
        }
        #endregion

        #region Overriden System.Object Members
        public override string ToString()
        {
            return vText == null ? "" : vText;
        }
        #endregion
    }
}
