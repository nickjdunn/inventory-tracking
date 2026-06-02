using System;
using System.Windows.Forms;

namespace MerlinStream
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
