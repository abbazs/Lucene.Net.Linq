﻿using System;
using System.Linq;
using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Linq.Abstractions;
using Lucene.Net.Linq.Mapping;
using Lucene.Net.Linq.Transformation;
using Lucene.Net.Store;
using Remotion.Linq.Parsing.ExpressionTreeVisitors.Transformation;
using Remotion.Linq.Parsing.Structure;
using Remotion.Linq.Parsing.Structure.ExpressionTreeProcessors;
using Version = Lucene.Net.Util.Version;

namespace Lucene.Net.Linq
{
    /// <summary>
    /// Provides IQueryable access to a Lucene.Net index as well as an API
    /// for adding, deleting and replacing documents within atomic transactions.
    /// </summary>
    public class LuceneDataProvider
    {
        private readonly Directory directory;
        private readonly Analyzer analyzer;
        private readonly Version version;
        private readonly IQueryParser queryParser;
        private readonly Context context;

        /// <summary>
        /// Constructs a new read-only instance without supplying an IndexWriter.
        /// </summary>
        /// <param name="directory"></param>
        /// <param name="analyzer"></param>
        /// <param name="version"></param>
        public LuceneDataProvider(Directory directory, Analyzer analyzer, Version version)
            : this(directory, analyzer, version, null, new object())
        {
        }

        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        /// <param name="directory"></param>
        /// <param name="analyzer"></param>
        /// <param name="version"></param>
        /// <param name="indexWriter"></param>
        public LuceneDataProvider(Directory directory, Analyzer analyzer, Version version, IndexWriter indexWriter)
            : this(directory, analyzer, version, new IndexWriterAdapter(indexWriter), new object())
        {
        }

        /// <summary>
        /// If the supplied IndexWriter will be written to outside of this instance of LuceneDataProvider,
        /// the <paramref name="transactionLock"/> will be used to coordinate writes.
        /// </summary>
        /// <param name="directory"></param>
        /// <param name="analyzer"></param>
        /// <param name="version"></param>
        /// <param name="indexWriter"></param>
        /// <param name="transactionLock"></param>
        public LuceneDataProvider(Directory directory, Analyzer analyzer, Version version, IIndexWriter indexWriter, object transactionLock)
        {
            this.directory = directory;
            this.analyzer = analyzer;
            this.version = version;

            queryParser = CreateQueryParser();
            context = new Context(this.directory, this.analyzer, this.version, indexWriter, transactionLock);
        }

        internal static QueryParser CreateQueryParser()
        {
            var expressionTreeParser = new ExpressionTreeParser(
                ExpressionTreeParser.CreateDefaultNodeTypeProvider(),
                CreateExpressionTreeProcessor());

            return new QueryParser(expressionTreeParser);
        }

        /// <summary>
        /// Creates an <c cref="IExpressionTreeProcessor"/> that will execute
        /// <c cref="AllowSpecialCharactersExpressionTransformer"/>
        /// before executing <c cref="PartialEvaluatingExpressionTreeProcessor"/>
        /// and other default processors. 
        /// </summary>
        internal static IExpressionTreeProcessor CreateExpressionTreeProcessor()
        {
            var firstRegistry = new ExpressionTransformerRegistry();
            firstRegistry.Register(new AllowSpecialCharactersExpressionTransformer());

            var processor = ExpressionTreeParser.CreateDefaultProcessor(ExpressionTransformerRegistry.CreateDefault());
            processor.InnerProcessors.Insert(0, new TransformingExpressionTreeProcessor(firstRegistry));
            return processor;
        }

        /// <summary>
        /// Returns an IQueryable implementation where the type being mapped
        /// from <c cref="Document"/> has a public default constructor.
        /// </summary>
        /// <typeparam name="T">The type of object that Document will be mapped onto.</typeparam>
        public IQueryable<T> AsQueryable<T>() where T : new()
        {
            return AsQueryable(() => new T());
        }

        /// <summary>
        /// Returns an IQueryable implementation where the type being mapped
        /// from <c cref="Document"/> is constructed by a factory delegate.
        /// </summary>
        /// <typeparam name="T">The type of object that Document will be mapped onto.</typeparam>
        /// <param name="factory">Factory method to instantiate new instances of T.</param>
        public IQueryable<T> AsQueryable<T>(Func<T> factory)
        {
            var executor = new QueryExecutor<T>(context, factory, new ReflectionDocumentMapper<T>());
            return new LuceneQueryable<T>(queryParser, executor);
        }

        /// <summary>
        /// Opens a session for staging changes and then committing them atomically.
        /// </summary>
        /// <typeparam name="T">The type of object that will be mapped to <c cref="Document"/>.</typeparam>
        public ISession<T> OpenSession<T>()
        {
            if (context.IsReadOnly)
            {
                throw new InvalidOperationException("This data provider is read-only. To enable writes, construct " + typeof(LuceneDataProvider) + " with an IndexWriter.");
            }

            var mapper = new ReflectionDocumentMapper<T>();
            return new LuceneSession<T>(mapper, context);
        }
    }
}