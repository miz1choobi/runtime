// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

using Internal.NativeFormat;
using Internal.Text;

namespace ILCompiler.DependencyAnalysis
{
    /// <summary>
    /// Represents a map between EETypes and metadata records within the <see cref="MetadataNode"/>.
    /// </summary>
    public sealed class TypeMetadataMapNode : ObjectNode, ISymbolDefinitionNode, INodeWithSize
    {
        private int? _size;
        private ExternalReferencesTableNode _externalReferences;

        public TypeMetadataMapNode(ExternalReferencesTableNode externalReferences)
        {
            _externalReferences = externalReferences;
        }

        int INodeWithSize.Size => _size.Value;

        public void AppendMangledName(NameMangler nameMangler, Utf8StringBuilder sb)
        {
            sb.Append(nameMangler.CompilationUnitPrefix).Append("__type_to_metadata_map"u8);
        }
        public int Offset => 0;
        public override bool IsShareable => false;

        public override ObjectNodeSection GetSection(NodeFactory factory) => _externalReferences.GetSection(factory);

        public override bool StaticDependenciesAreComputed => true;

        protected override string GetName(NodeFactory factory) => this.GetMangledName(factory.NameMangler);

        public override ObjectData GetData(NodeFactory factory, bool relocsOnly = false)
        {
            // This node does not trigger generation of other nodes.
            if (relocsOnly)
                return new ObjectData(Array.Empty<byte>(), Array.Empty<Relocation>(), 1, new ISymbolDefinitionNode[] { this });

            var writer = new NativeWriter();
            var typeMapHashTable = new VertexHashtable();

            Section hashTableSection = writer.NewSection();
            hashTableSection.Place(typeMapHashTable);

            foreach (var mappingEntry in factory.MetadataManager.GetTypeDefinitionMapping(factory))
            {
                // Types that don't have EETypes don't need mapping table entries because there's no risk of them
                // not unifying to the same System.Type at runtime.
                if (!factory.MetadataManager.TypeGeneratesEEType(mappingEntry.Entity) && !factory.CompilationModuleGroup.ShouldReferenceThroughImportTable(mappingEntry.Entity))
                    continue;

                // Go with a necessary type symbol. It will be upgraded to a constructed one if a constructed was emitted.
                IEETypeNode typeSymbol = factory.NecessaryTypeSymbol(mappingEntry.Entity);

                Vertex vertex = writer.GetTuple(
                    writer.GetUnsignedConstant(_externalReferences.GetIndex(typeSymbol)),
                    writer.GetUnsignedConstant((uint)mappingEntry.MetadataHandle)
                    );

                int hashCode = typeSymbol.Type.GetHashCode();
                typeMapHashTable.Append((uint)hashCode, hashTableSection.Place(vertex));
            }

            byte[] hashTableBytes = writer.Save();

            _size = hashTableBytes.Length;

            return new ObjectData(hashTableBytes, Array.Empty<Relocation>(), 1, new ISymbolDefinitionNode[] { this });
        }

        protected internal override int Phase => (int)ObjectNodePhase.Ordered;

        public override int ClassCode => (int)ObjectNodeOrder.TypeMetadataMapNode;
    }
}
