using System.Collections.Generic;
using System.IO;

namespace NongFormat
{
    /// <summary>Provide iterator for directory traversal.</summary>
    public class DirTraverser : IEnumerable<string>
    {
        private string[] dirs;
        private int index;

        public DirTraverser (string root)
        { this.dirs = new string[] { root }; }

        private DirTraverser (string[] dirs)
        { this.dirs = dirs; }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        { return GetEnumerator(); }

        public IEnumerator<string> GetEnumerator()
        {
            var stack = new Stack<DirTraverser>();
            for (var node = this;;)
            {
                string dirName = node.dirs[node.index];
                yield return dirName;

                if (Directory.Exists (dirName))
                {
                    string[] subdirs = Directory.GetDirectories (dirName);
                    if (subdirs.Length > 0)
                    {
                        stack.Push (node);
                        node = new DirTraverser (subdirs);
                        continue;
                    }
                }

                for (; ++node.index >= node.dirs.Length; node = stack.Pop())
                    if (stack.Count == 0)
                        yield break;
            }
        }
    }
}
