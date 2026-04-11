using System.Collections.Generic;

namespace _5eApiTranslator.Models
{
    internal class AuroraImportCatalog
    {
        public List<AuroraFileInfo> Files { get; set; } = new();
        public List<AuroraElement> Elements { get; set; } = new();
        public List<AuroraSpell> Spells { get; set; } = new();
    }
}
