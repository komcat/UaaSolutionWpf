using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Serilog;
using UaaSolutionWpf.Commands;

namespace UaaSolutionWpf.Commands
{
    /// <summary>
    /// Command to capture an image from the camera and save it to a file
    /// </summary>
    public class CameraImageCaptureCommand : Command<CameraManagerWpf>
    {
        private readonly string _prefix;
        private readonly bool _isForRecording;
        private readonly string _specificFilename;

        /// <summary>
        /// Creates a new command to capture an image from the camera
        /// </summary>
        /// <param name="cameraManager">The camera manager to use</param>
        /// <param name="prefix">The prefix for the filename</param>
        /// <param name="isForRecording">True for recording (rolling filenames), false for reference (datestamp filenames)</param>
        /// <param name="specificFilename">Optional specific filename to use instead of auto-generated one</param>
        /// <param name="logger">Optional logger</param>
        public CameraImageCaptureCommand(
            CameraManagerWpf cameraManager,
            string prefix,
            bool isForRecording,
            string specificFilename = null,
            ILogger logger = null)
            : base(
                cameraManager,
                $"CameraCapture-{prefix}-{(isForRecording ? "Recording" : "Reference")}",
                $"Capture camera image with prefix {prefix} for {(isForRecording ? "recording" : "reference")}",
                logger)
        {
            _prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
            _isForRecording = isForRecording;
            _specificFilename = specificFilename;
        }

        protected override async Task<CommandResult> ExecuteInternalAsync()
        {
            try
            {
                if (_context == null)
                {
                    return CommandResult.Failed("Camera manager is not available");
                }

                // Get the current image from the camera
                var image = _context.CurrentImage;
                if (image == null)
                {
                    return CommandResult.Failed("No image is available from the camera");
                }

                // Check cancellation before saving the image
                _cancellationToken.ThrowIfCancellationRequested();

                // Generate the filename and ensure the directory exists
                string filepath = GenerateFilepath();
                Directory.CreateDirectory(Path.GetDirectoryName(filepath));

                _logger.Information("Saving camera image to {Filepath}", filepath);

                // Save the image to the file
                using (var fileStream = new FileStream(filepath, FileMode.Create))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(image));
                    encoder.Save(fileStream);
                }

                return CommandResult.Successful($"Image captured and saved to {filepath}");
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("Image capture operation was canceled");
                return CommandResult.Failed("Image capture was canceled");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error capturing camera image");
                return CommandResult.Failed($"Error capturing image: {ex.Message}", ex);
            }
        }

        private string GenerateFilepath()
        {
            // If a specific filename was provided, use it
            if (!string.IsNullOrEmpty(_specificFilename))
            {
                if (_isForRecording)
                {
                    return Path.Combine("Records", "Images", _specificFilename);
                }
                else
                {
                    return Path.Combine("Recipe", "Images", _specificFilename);
                }
            }

            // Otherwise, generate the filename based on the type of capture
            if (_isForRecording)
            {
                // For recording: prefix_timestamp.PNG
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                string filename = $"{_prefix}_{timestamp}.png";
                return Path.Combine("Records", "Images", filename);
            }
            else
            {
                // For reference: prefix_YYMMDD.PNG
                string datestamp = DateTime.Now.ToString("yyMMdd");
                string filename = $"{_prefix}_{datestamp}.png";
                return Path.Combine("Recipe", "Images", filename);
            }
        }
    }

    /// <summary>
    /// Command to capture multiple images in sequence
    /// </summary>
    public class CameraImageBurstCaptureCommand : Command<CameraManagerWpf>
    {
        private readonly string _prefix;
        private readonly int _count;
        private readonly TimeSpan _delay;

        /// <summary>
        /// Creates a new command to capture multiple images in sequence
        /// </summary>
        /// <param name="cameraManager">The camera manager to use</param>
        /// <param name="prefix">The prefix for the filenames</param>
        /// <param name="count">The number of images to capture</param>
        /// <param name="delay">The delay between captures</param>
        /// <param name="logger">Optional logger</param>
        public CameraImageBurstCaptureCommand(
            CameraManagerWpf cameraManager,
            string prefix,
            int count,
            TimeSpan delay,
            ILogger logger = null)
            : base(
                cameraManager,
                $"CameraBurstCapture-{prefix}-{count}",
                $"Capture {count} camera images with prefix {prefix}",
                logger)
        {
            _prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
            _count = count > 0 ? count : throw new ArgumentOutOfRangeException(nameof(count), "Count must be greater than 0");
            _delay = delay;
        }

        protected override async Task<CommandResult> ExecuteInternalAsync()
        {
            try
            {
                if (_context == null)
                {
                    return CommandResult.Failed("Camera manager is not available");
                }

                _logger.Information("Starting burst capture of {Count} images with prefix {Prefix}", _count, _prefix);

                // Create a subdirectory with timestamp for this burst
                string burstTimestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string burstDir = Path.Combine("Records", "Images", $"Burst_{_prefix}_{burstTimestamp}");
                Directory.CreateDirectory(burstDir);

                int capturedCount = 0;
                for (int i = 0; i < _count; i++)
                {
                    // Check cancellation before each capture
                    _cancellationToken.ThrowIfCancellationRequested();

                    // Check for pause
                    await CheckPausedAsync();

                    // Generate filename for this image in the sequence
                    string filename = $"{_prefix}_{i + 1:D3}.png";
                    string filepath = Path.Combine(burstDir, filename);

                    // Get the current image from the camera
                    var image = _context.CurrentImage;
                    if (image != null)
                    {
                        // Save the image to the file
                        using (var fileStream = new FileStream(filepath, FileMode.Create))
                        {
                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(image));
                            encoder.Save(fileStream);
                        }

                        capturedCount++;
                        _logger.Debug("Captured image {Count}/{Total}: {Filename}", i + 1, _count, filename);
                    }
                    else
                    {
                        _logger.Warning("Failed to capture image {Count}/{Total}: No image available", i + 1, _count);
                    }

                    // Wait for the specified delay before the next capture (except for the last one)
                    if (i < _count - 1 && _delay > TimeSpan.Zero)
                    {
                        await Task.Delay(_delay, _cancellationToken);
                    }
                }

                return CommandResult.Successful($"Captured {capturedCount} of {_count} images in burst to {burstDir}");
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("Burst capture operation was canceled");
                return CommandResult.Failed("Burst capture was canceled");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during burst capture");
                return CommandResult.Failed($"Error during burst capture: {ex.Message}", ex);
            }
        }
    }
}