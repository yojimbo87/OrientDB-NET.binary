﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Orient.Client.Protocol;
using Orient.Client.Protocol.Operations;

namespace Orient.Client.API
{
  

    public class OTransaction
    {
        private readonly Connection _connection;

        internal OTransaction(Connection connection)
        {
            _connection = connection;
            _tempClusterId = -1;
            _tempObjectId = -1;
        }

        private readonly Dictionary<ORID, TransactionRecord> _records = new Dictionary<ORID, TransactionRecord>();
        private readonly short _tempClusterId;
        private long _tempObjectId;

        public void Commit()
        {
            CommitTransaction ct = new CommitTransaction(_records.Values.ToList(), _connection.Database);
            var result = _connection.ExecuteOperation(ct);
            Dictionary<ORID, ORID> mapping = result.GetField<Dictionary<ORID, ORID>>("CreatedRecordMapping");

            foreach (var kvp in mapping)
            {
                var record = _records[kvp.Key];
                record.ORID = kvp.Value;
            }

            var versions = result.GetField<Dictionary<ORID, int>>("UpdatedRecordVersions");
            foreach (var kvp in versions)
            {
                var record = _records[kvp.Key];
                record.Version = kvp.Value;
            }

        }

        public void Reset()
        {
            _records.Clear();
        }

        public void Add(ODocument document)
        {
            var record = new TransactionRecord(RecordType.Create, document);
            Insert(record);
        }

        public void Add<T>(T typedObject) where T : IBaseRecord
        {
            var record = new TypedTransactionRecord<T>(RecordType.Create, typedObject);
            Insert(record);
        }

        public void Update(ODocument document)
        {
            var record = new TransactionRecord(RecordType.Update, document);
            Insert(record);
        }

        public void Update<T>(T typedObject) where T : IBaseRecord
        {
            var record = new TypedTransactionRecord<T>(RecordType.Update, typedObject);
            Insert(record);
        }

        public void Delete(ODocument document)
        {
            var record = new TransactionRecord(RecordType.Delete, document);
            Insert(record);
        }

        public void Delete<T>(T typedObject) where T : IBaseRecord
        {
            var record = new TypedTransactionRecord<T>(RecordType.Delete, typedObject);
            Insert(record);
        }

        private void Insert(TransactionRecord record)
        {
            bool hasOrid = record.ORID != null;
            bool needsOrid = record.RecordType != RecordType.Create;

            if (hasOrid && !needsOrid)
                throw new InvalidOperationException("Objects to be added via a transaction must not already be in the database");

            if (needsOrid && !hasOrid)
                throw new InvalidOperationException("Objects to be updated or deleted via a transaction must already be in the database");

            if (!hasOrid)
            {
                record.ORID = CreateTempORID();
                record.ORID.ClusterId = _connection.Database.GetClusterIdFor(record.OClassName);
            }

            if (_records.ContainsKey(record.ORID))
            {
                if (record.RecordType != _records[record.ORID].RecordType)
                    throw new InvalidOperationException("Same object already part of transaction with a different CRUD intent");
                _records[record.ORID] = record;
            }
            else
            {
                _records.Add(record.ORID, record);
            }
        }

        private ORID CreateTempORID()
        {
            return new ORID(_tempClusterId, --_tempObjectId);
        }
    }
}