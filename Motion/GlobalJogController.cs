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
            HexapodMovementService rightHexapod,            
            GantryMovementService gantry,
            ILogger logger,
            HexapodMovementService bottomHexapod = null,
            HexapodMovementService leftHexapod = null)
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
                0, 0, 1, 0,  // X -> Z, correct X->Z
                0, -1, 0, 0,  // Y -> X , correct  Y-> -Y
                1, 0, 0, 0,  // Z -> Y , correct  Z->X
                0, 0, 0, 1
            );

            // Right Hexapod: Global to Local
            // Global (X,Y,Z) -> Local (Z,-X,-Y)
            _rightHexapodTransform = new Matrix4x4(
                0, 0, -1, 0,   // X -> -Z
                0, 1, 0, 0,  // Y -> -X correct Y -> Y
                1, 0, 0, 0,  // Z -> -Y correct Z -> X
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
                    tasks.Add(MoveHexapod(_leftHexapodService, leftLocal, Vector3.Zero, "Left Hexapod"));
                }

                if (applyToRightHexapod)
                {
                    Vector3 rightLocal = TransformVector(globalMovement, _rightHexapodTransform);
                    tasks.Add(MoveHexapod(_rightHexapodService, rightLocal, Vector3.Zero, "Right Hexapod"));
                }

                if (applyToBottomHexapod)
                {
                    Vector3 bottomLocal = TransformVector(globalMovement, _bottomHexapodTransform);
                    tasks.Add(MoveHexapod(_bottomHexapodService, bottomLocal, Vector3.Zero, "Bottom Hexapod"));
                }

                if (applyToGantry)
                {
                    Vector3 gantryLocal = TransformVector(globalMovement, _gantryTransform);
                    tasks.Add(MoveGantry(gantryLocal));
                }

                await Task.WhenAll(tasks);
                _logger.Information("Completed global jog movement");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during global jog movement");
                throw;
            }
        }

        public async Task JogRotation(Vector3 rotation, bool applyToLeftHexapod = false, bool applyToRightHexapod = false,
            bool applyToBottomHexapod = false)
        {
            try
            {
                _logger.Information("Starting rotation movement: {Rotation}", rotation);

                var tasks = new List<Task>();

                if (applyToLeftHexapod)
                {
                    Vector3 leftRotation = TransformVector(rotation, _leftHexapodTransform);
                    tasks.Add(MoveHexapod(_leftHexapodService, Vector3.Zero, leftRotation, "Left Hexapod"));
                }

                if (applyToRightHexapod)
                {
                    Vector3 rightRotation = TransformVector(rotation, _rightHexapodTransform);
                    tasks.Add(MoveHexapod(_rightHexapodService, Vector3.Zero, rightRotation, "Right Hexapod"));
                }

                if (applyToBottomHexapod)
                {
                    Vector3 bottomRotation = TransformVector(rotation, _bottomHexapodTransform);
                    tasks.Add(MoveHexapod(_bottomHexapodService, Vector3.Zero, bottomRotation, "Bottom Hexapod"));
                }

                await Task.WhenAll(tasks);
                _logger.Information("Completed rotation movement");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during rotation movement");
                throw;
            }
        }

        private Vector3 TransformVector(Vector3 vector, Matrix4x4 transform)
        {
            Vector4 vector4 = new Vector4(vector.X, vector.Y, vector.Z, 1);
            vector4 = Vector4.Transform(vector4, transform);
            return new Vector3(vector4.X, vector4.Y, vector4.Z);
        }

        private async Task MoveHexapod(HexapodMovementService service, Vector3 translation, Vector3 rotation, string deviceName)
        {
            try
            {
                _logger.Debug("Moving {DeviceName} - Translation: {Translation}, Rotation: {Rotation}",
                    deviceName, translation, rotation);

                var tasks = new List<Task>();

                // Handle translation movements
                if (translation != Vector3.Zero)
                {
                    if (Math.Abs(translation.X) > 0.00001)
                        tasks.Add(service.MoveRelativeAsync(HexapodMovementService.Axis.X, translation.X));
                    if (Math.Abs(translation.Y) > 0.00001)
                        tasks.Add(service.MoveRelativeAsync(HexapodMovementService.Axis.Y, translation.Y));
                    if (Math.Abs(translation.Z) > 0.00001)
                        tasks.Add(service.MoveRelativeAsync(HexapodMovementService.Axis.Z, translation.Z));
                }

                // Handle rotation movements
                if (rotation != Vector3.Zero)
                {
                    if (Math.Abs(rotation.X) > 0.00001)
                        tasks.Add(service.MoveRelativeAsync(HexapodMovementService.Axis.U, rotation.X));
                    if (Math.Abs(rotation.Y) > 0.00001)
                        tasks.Add(service.MoveRelativeAsync(HexapodMovementService.Axis.V, rotation.Y));
                    if (Math.Abs(rotation.Z) > 0.00001)
                        tasks.Add(service.MoveRelativeAsync(HexapodMovementService.Axis.W, rotation.Z));
                }

                await Task.WhenAll(tasks);
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

                var tasks = new List<Task>();

                // Apply movements to each axis that has a non-zero value
                if (Math.Abs(movement.X) > 0.00001)
                    tasks.Add(_gantryService.MoveRelativeAsync(0, movement.X));
                if (Math.Abs(movement.Y) > 0.00001)
                    tasks.Add(_gantryService.MoveRelativeAsync(1, movement.Y));
                if (Math.Abs(movement.Z) > 0.00001)
                    tasks.Add(_gantryService.MoveRelativeAsync(2, movement.Z));

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving Gantry");
                throw;
            }
        }
    }
}