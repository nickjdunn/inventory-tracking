using System;
using System.Windows.Forms;

namespace MerlinAudit
{
    static class Program
    {
        [MTAThread]
        static void Main()
        {
            Application.Run(new MainForm());
        }
    }
}
