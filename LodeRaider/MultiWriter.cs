using System;
using System.IO;
using System.Text;

namespace LodeRaider
{
    // Create a custom writer class
    public class MultiWriter : TextWriter
    {
        private readonly TextWriter[] writers;
        public override Encoding Encoding => Encoding.UTF8;

        public MultiWriter(params TextWriter[] writers)
        {
            this.writers = writers ?? throw new ArgumentNullException(nameof(writers));
        }

        public override void Write(char value)
        {
            foreach (var writer in writers)
                writer?.Write(value);
        }

        public override void WriteLine(string? value)
        {
            foreach (var writer in writers)
                writer?.WriteLine(value);
        }

        public override void Flush()
        {
            foreach (var writer in writers)
                writer?.Flush();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var writer in writers)
                    writer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
