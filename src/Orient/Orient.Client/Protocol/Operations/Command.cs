﻿using System.Collections.Generic;
using System.Linq;
using Orient.Client.Protocol;
using Orient.Client.Protocol.Serializers;

namespace Orient.Client.Protocol.Operations
{
    internal class Command : IOperation
    {
        internal OperationMode OperationMode { get; set; }
        internal CommandClassType ClassType { get; set; }
        internal CommandPayload CommandPayload { get; set; }

        public Request Request(int sessionId)
        {
            Request request = new Request();
            
            // standard request fields
            request.DataItems.Add(new RequestDataItem() { Type = "byte", Data = BinarySerializer.ToArray((byte)OperationType.COMMAND) });
            request.DataItems.Add(new RequestDataItem() { Type = "int", Data = BinarySerializer.ToArray(sessionId) });
            // operation specific fields
            request.DataItems.Add(new RequestDataItem() { Type = "byte", Data = BinarySerializer.ToArray((byte)OperationMode) });

            // class name field
            string className = "x";
            switch (ClassType)
            {
                // idempotent command (e.g. select)
                case CommandClassType.Idempotent:
                    className = "com.orientechnologies.orient.core.sql.query.OSQLSynchQuery";
                    break;
                // non-idempotent command (e.g. insert)
                case CommandClassType.NonIdempotent:
                    className = "com.orientechnologies.orient.core.sql.OCommandSQL";
                    break;
                // script command
                case CommandClassType.Script:
                    className = "com.orientechnologies.orient.core.command.script.OCommandScript";
                    break;
                default:
                    break;
            }

            // TODO: sql script case length
            request.DataItems.Add(new RequestDataItem() { Type = "int", Data = BinarySerializer.ToArray(
                //4 + // this int
                4 + // class name int length
                BinarySerializer.Length(className) + 
                4 + // limit int length
                4 + // text int length
                BinarySerializer.Length(CommandPayload.Text) + 
                4 + // fetch plant int length
                BinarySerializer.Length(CommandPayload.FetchPlan) +
                4 // serialized params int (disable)
            ) });
            request.DataItems.Add(new RequestDataItem() { Type = "string", Data = BinarySerializer.ToArray(className) });

            if (CommandPayload.Type == CommandPayloadType.SqlScript)
            {
                request.DataItems.Add(new RequestDataItem() { Type = "string", Data = BinarySerializer.ToArray(CommandPayload.Language) });
            }

            request.DataItems.Add(new RequestDataItem() { Type = "string", Data = BinarySerializer.ToArray(CommandPayload.Text) });
            request.DataItems.Add(new RequestDataItem() { Type = "int", Data = BinarySerializer.ToArray(CommandPayload.NonTextLimit) });
            request.DataItems.Add(new RequestDataItem() { Type = "string", Data = BinarySerializer.ToArray(CommandPayload.FetchPlan) });
            //request.DataItems.Add(new RequestDataItem() { Type = "bytes", Data = CommandPayload.SerializedParams });
            // HACK: 0:int means disable
            request.DataItems.Add(new RequestDataItem() { Type = "int", Data = BinarySerializer.ToArray(0) });
            
            return request;
        }

        public ODocument Response(Response response)
        {
            // start from this position since standard fields (status, session ID) has been already parsed
            int offset = 5;
            ODocument responseDocument = new ODocument();
            
            if (response == null)
            {
                return responseDocument;
            }

            // operation specific fields
            PayloadStatus payloadStatus = (PayloadStatus)BinarySerializer.ToByte(response.Data.Skip(offset).Take(1).ToArray());
            offset += 1;

            responseDocument.SetField("PayloadStatus", payloadStatus);

            if (OperationMode == OperationMode.Asynchronous)
            {
                List<ODocument> documents = new List<ODocument>();

                while (payloadStatus != PayloadStatus.NoRemainingRecords)
                {
                    ODocument document = ParseDocument(ref offset, response.Data);

                    switch (payloadStatus)
                    {
                        case PayloadStatus.ResultSet:
                            documents.Add(document);
                            break;
                        case PayloadStatus.PreFetched:
                            // TODO: client cache
                            documents.Add(document);
                            break;
                        default:
                            break;
                    }

                    payloadStatus = (PayloadStatus)BinarySerializer.ToByte(response.Data.Skip(offset).Take(1).ToArray());
                    offset += 1;
                }

                responseDocument.SetField("Content", documents);
            }
            else
            {
                int contentLength;

                switch (payloadStatus)
                {
                    case PayloadStatus.NullResult: // 'n'
                        // nothing to do
                        break;
                    case PayloadStatus.SingleRecord: // 'r'
                        ODocument document = ParseDocument(ref offset, response.Data);
                        responseDocument.SetField("Content", document);
                        break;
                    case PayloadStatus.SerializedResult: // 'a'
                        // TODO: how to parse result - string?
                        contentLength = BinarySerializer.ToInt(response.Data.Skip(offset).Take(4).ToArray());
                        offset += 4;
                        string serialized = BinarySerializer.ToString(response.Data.Skip(offset).Take(contentLength).ToArray());
                        offset += contentLength;

                        responseDocument.SetField("Content", serialized);
                        break;
                    case PayloadStatus.RecordCollection: // 'l'
                        int recordsCount = BinarySerializer.ToInt(response.Data.Skip(offset).Take(4).ToArray());
                        offset += 4;

                        List<ODocument> documents = new List<ODocument>();

                        for (int i = 0; i < recordsCount; i++)
                        {
                            documents.Add(ParseDocument(ref offset, response.Data));
                        }

                        responseDocument.SetField("Content", documents);
                        break;
                    default:
                        break;
                }
            }

            return responseDocument;
        }

        private ODocument ParseDocument(ref int offset, byte[] data)
        {
            ODocument document = null;

            short classId = BinarySerializer.ToShort(data.Skip(offset).Take(2).ToArray());
            offset += 2;

            if (classId == -2) // NULL
            {
            }
            else if (classId == -3) // record id
            {
                ORID orid = new ORID();
                orid.ClusterId = BinarySerializer.ToShort(data.Skip(offset).Take(2).ToArray());
                offset += 2;

                orid.ClusterPosition = BinarySerializer.ToLong(data.Skip(offset).Take(8).ToArray());
                offset += 8;

                document = new ODocument();
                document.ORID = orid;
                document.OClassId = classId;
            }
            else
            {
                ORecordType type = (ORecordType)BinarySerializer.ToByte(data.Skip(offset).Take(1).ToArray());
                offset += 1;

                ORID orid = new ORID();
                orid.ClusterId = BinarySerializer.ToShort(data.Skip(offset).Take(2).ToArray());
                offset += 2;

                orid.ClusterPosition = BinarySerializer.ToLong(data.Skip(offset).Take(8).ToArray());
                offset += 8;

                int version = BinarySerializer.ToInt(data.Skip(offset).Take(4).ToArray());
                offset += 4;

                int recordLength = BinarySerializer.ToInt(data.Skip(offset).Take(4).ToArray());
                offset += 4;

                byte[] rawRecord = data.Skip(offset).Take(recordLength).ToArray();
                offset += recordLength;

                document = RecordSerializer.Deserialize(orid, version, type, classId, rawRecord);
            }

            return document;
        }
    }
}
