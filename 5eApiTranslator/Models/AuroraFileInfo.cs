using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AuroraTranslator.Models
{
    internal class AuroraFileInfo
    {
        public string RelativePath { get; set; }
        /// <summary>Absolute path on disk — used for hash computation; not stored in the DB.</summary>
        public string FullPath { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public Author Author { get; set; }
        public FileVersion FileVersion { get; set; }
    }
}

