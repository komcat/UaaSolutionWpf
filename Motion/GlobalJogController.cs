using System;
using System.Numerics;
using Serilog;
using UaaSolutionWpf.Services;

namespace UaaSolutionWpf.Motion
{
    public class GlobalJogController
    {
        private readonly ILogger _logger;
        private readonly HexapodMovementService _leftHexapodService;
        private readonly HexapodMovementService _rightHexapodService;
        private readonly HexapodMovementService _bottomHexapodService;
        private readonly GantryMovementService _gantryService;

        // Transformation matrices from global to local coordinates
        private Matrix4x4 _leftHexapodTransform;
        private Matrix4x4 _rightHexapodTransform;
        private Matrix4x4 _bottomHexapodTransform;
        private Matrix4x4 _gantryTransform;

        public GlobalJogController(
            HexapodMovementService leftHexapod,
            HexapodMovementService rightHexapod,
            HexapodMovementService bottomHexapod,
            GantryMovementService gantry,
            ILogger logger)
        {
            _leftHexapodService = leftHexapod;
            _rightHexapodService = rightHexapod;
            _bottomHexapodService = bottomHexapod;
            _gantryService = gantry;
            _logger = logger.ForContext<GlobalJogController>();

            InitializeTransformationMatrices();
        }

        private void InitializeTransformationMatrices()
        {
            // Left Hexapod: Global to Local
            // Global (X,Y,Z) -> Local (Z,X,Y)
            _leftHexapodTransform = new Matrix4x4(
                0, 0, 1, 0,  // X -> Z
                1, 0, 0, 0,  // Y -> X
                0, 1, 0, 0,  // Z -> Y
                0, 0, 0, 1
            );

            // Right Hexapod: Global to Local
            // Global (X,Y,Z) -> Local (Z,-X,-Y)
            _rightHexapodTransform = new Matrix4x4(
                0, 0, 1, 0,   // X -> Z
                -1, 0, 0, 0,  // Y -> -X
                0, -1, 0, 0,  // Z -> -Y
                0, 0, 0, 1
            );

            // Bottom Hexapod: Global to Local
            // Global (X,Y,Z) -> Local (X,Y,Z)
            _bottomHexapodTransform = Matrix4x4.Identity;

            // Gantry: Global to Local
            // Global (X,Y,Z) -> Local (X,-Y,-Z)
            _gantryTransform = new Matrix4x4(
                1, 0, 0, 0,   // X -> X
                0, -1, 0, 0,  // Y -> -Y
                0, 0, -1, 0,  // Z -> -Z
                0, 0, 0, 1
            );
        }

        public async Task JogGlobal(Vector3 globalMovement, bool applyToLeftHexapod = true, bool applyToRightHexapod = true,
            bool applyToBottomHexapod = true, bool applyToGantry = true)
        {
            try
            {
                _logger.Information("Starting global jog movement: {GlobalMovement}", globalMovement);

                var tasks = new List<Task>();

                // Transform and apply movement to each device
                if (applyToLeftHexapod)
                {
                    Vector3 leftLocal = TransformVector(globalMovement, _leftHexapodTransform);
                    tasks.Add(MoveHexapod(_leftHexapodService, leftLocal, "Left Hexapod"));
                }

                if (applyToRightHexapod)
                {
                    Vector3 rightLocal = TransformVector(globalMovement, _rightHexapodTransform);
                    tasks.Add(MoveHexapod(_rightHexapodService, rightLocal, "Right Hexapod"));
                }

                if (applyToBottomHexapod)
                {
                    Vector3 bottomLocal = TransformVector(globalMovement, _bottomHexapodTransform);
                    tasks.Add(MoveHexapod(_bottomHexapodService, bottomLocal, "Bottom Hexapod"));
                }

                if (applyToGantry)
                {
                    Vector3 gantryLocal = TransformVector(globalMovement, _gantryTransform);
                    tasks.Add(MoveGantry(gantryLocal));
                }

                // Wait for all movements to complete
                await Task.WhenAll(tasks);
                _logger.Information("Completed global jog movement");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during global jog movement");
                throw;
            }
        }

        private Vector3 TransformVector(Vector3 vector, Matrix4x4 transform)
        {
            Vector4 vector4 = new Vector4(vector.X, vector.Y, vector.Z, 1);
            vector4 = Vector4.Transform(vector4, transform);
            return new Vector3(vector4.X, vector4.Y, vector4.Z);
        }

        private async Task MoveHexapod(HexapodMovementService service, Vector3 movement, string deviceName)
        {
            try
            {
                _logger.Debug("Moving {DeviceName} by {Movement}", deviceName, movement);

                // Create relative movement array (X,Y,Z,U,V,W) - only translational movement for now
                double[] relativeMove = new double[] {
                    movement.X,
                    movement.Y,
                    movement.Z,
                    0, // U (rotation)
                    0, // V (rotation)
                    0  // W (rotation)
                };

                // Apply movements to each axis that has a non-zero value
                for (int i = 0; i < 3; i++) // Only moving translational axes for now
                {
                    if (Math.Abs(relativeMove[i]) > 0.00001) // Small threshold to avoid unnecessary movements
                    {
                        await service.MoveRelativeAsync((HexapodMovementService.Axis)i, relativeMove[i]);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving {DeviceName}", deviceName);
                throw;
            }
        }

        private async Task MoveGantry(Vector3 movement)
        {
            try
            {
                _logger.Debug("Moving Gantry by {Movement}", movement);

                // Apply movements to each axis that has a non-zero value
                for (int i = 0; i < 3; i++)
                {
                    if (Math.Abs(movement[i]) > 0.00001) // Small threshold to avoid unnecessary movements
                    {
                        await _gantryService.MoveRelativeAsync(i, movement[i]);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving Gantry");
                throw;
            }
        }
    }
}