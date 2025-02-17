using System;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using Serilog;
using UaaSolutionWpf.ViewModels;
using UaaSolutionWpf.Services;
using UaaSolutionWpf.IO;

namespace UaaSolutionWpf.Controls
{
    public partial class PneumaticSlideTestControl : UserControl
    {
        private readonly ILogger _logger;
        private readonly PneumaticSlideService _slideService;
        private readonly ObservableCollection<SlideTestViewModel> _slides;

        public PneumaticSlideTestControl()
        {
            InitializeComponent();
            _slides = new ObservableCollection<SlideTestViewModel>();
            SlideList.ItemsSource = _slides;
        }

        public void Initialize(PneumaticSlideService slideService, ILogger logger)
        {
            try
            {
                _slides.Clear();

                // Create view models for each slide
                foreach (var slide in slideService.GetSlideConfigurations())
                {
                    var viewModel = new SlideTestViewModel(
                        slide.Id,
                        slide.Name,
                        slideService
                    );
                    _slides.Add(viewModel);
                }

                logger.Information("Initialized PneumaticSlideTestControl with {Count} slides", _slides.Count);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error initializing PneumaticSlideTestControl");
                throw;
            }
        }
    }
}