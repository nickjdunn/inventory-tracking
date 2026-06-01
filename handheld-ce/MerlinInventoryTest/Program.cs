using System;
using System.Windows.Forms;

namespace MerlinInventoryTest
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
