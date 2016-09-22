// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.JsonPatch.Exceptions;
using Microsoft.AspNetCore.JsonPatch.Helpers;
using Microsoft.AspNetCore.JsonPatch.Internal;
using Microsoft.AspNetCore.JsonPatch.Operations;
using Newtonsoft.Json.Serialization;

namespace Microsoft.AspNetCore.JsonPatch.Adapters
{
    /// <inheritdoc />
    public class ObjectAdapter : IObjectAdapter
    {
        /// <summary>
        /// Initializes a new instance of <see cref="ObjectAdapter"/>.
        /// </summary>
        /// <param name="contractResolver">The <see cref="IContractResolver"/>.</param>
        /// <param name="logErrorAction">The <see cref="Action"/> for logging <see cref="JsonPatchError"/>.</param>
        public ObjectAdapter(
            IContractResolver contractResolver,
            Action<JsonPatchError> logErrorAction)
        {
            if (contractResolver == null)
            {
                throw new ArgumentNullException(nameof(contractResolver));
            }

            ContractResolver = contractResolver;
            LogErrorAction = logErrorAction;
        }

        /// <summary>
        /// Gets or sets the <see cref="IContractResolver"/>.
        /// </summary>
        public IContractResolver ContractResolver { get; }

        /// <summary>
        /// Action for logging <see cref="JsonPatchError"/>.
        /// </summary>
        public Action<JsonPatchError> LogErrorAction { get; }

        /// <summary>
        /// The "add" operation performs one of the following functions,
        /// depending upon what the target location references:
        ///
        /// o  If the target location specifies an array index, a new value is
        ///    inserted into the array at the specified index.
        ///
        /// o  If the target location specifies an object member that does not
        ///    already exist, a new member is added to the object.
        ///
        /// o  If the target location specifies an object member that does exist,
        ///    that member's value is replaced.
        ///
        /// The operation object MUST contain a "value" member whose content
        /// specifies the value to be added.
        ///
        /// For example:
        ///
        /// { "op": "add", "path": "/a/b/c", "value": [ "foo", "bar" ] }
        ///
        /// When the operation is applied, the target location MUST reference one
        /// of:
        ///
        /// o  The root of the target document - whereupon the specified value
        ///    becomes the entire content of the target document.
        ///
        /// o  A member to add to an existing object - whereupon the supplied
        ///    value is added to that object at the indicated location.  If the
        ///    member already exists, it is replaced by the specified value.
        ///
        /// o  An element to add to an existing array - whereupon the supplied
        ///    value is added to the array at the indicated location.  Any
        ///    elements at or above the specified index are shifted one position
        ///    to the right.  The specified index MUST NOT be greater than the
        ///    number of elements in the array.  If the "-" character is used to
        ///    index the end of the array (see [RFC6901]), this has the effect of
        ///    appending the value to the array.
        ///
        /// Because this operation is designed to add to existing objects and
        /// arrays, its target location will often not exist.  Although the
        /// pointer's error handling algorithm will thus be invoked, this
        /// specification defines the error handling behavior for "add" pointers
        /// to ignore that error and add the value as specified.
        ///
        /// However, the object itself or an array containing it does need to
        /// exist, and it remains an error for that not to be the case.  For
        /// example, an "add" with a target location of "/a/b" starting with this
        /// document:
        ///
        /// { "a": { "foo": 1 } }
        ///
        /// is not an error, because "a" exists, and "b" will be added to its
        /// value.  It is an error in this document:
        ///
        /// { "q": { "bar": 2 } }
        ///
        /// because "a" does not exist.
        /// </summary>
        /// <param name="operation">The add operation.</param>
        /// <param name="objectToApplyTo">Object to apply the operation to.</param>
        public void Add(Operation operation, object objectToApplyTo)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            if (objectToApplyTo == null)
            {
                throw new ArgumentNullException(nameof(objectToApplyTo));
            }

            Add(operation.path, operation.value, objectToApplyTo, operation);
        }

        /// <summary>
        /// Add is used by various operations (eg: add, copy, ...), yet through different operations;
        /// This method allows code reuse yet reporting the correct operation on error
        /// </summary>
        private void Add(
            string path,
            object value,
            object objectToApplyTo,
            Operation operationToReport)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (objectToApplyTo == null)
            {
                throw new ArgumentNullException(nameof(objectToApplyTo));
            }

            if (operationToReport == null)
            {
                throw new ArgumentNullException(nameof(operationToReport));
            }

            try
            {
                var patchObject = ObjectTreeAnalyzer.Analyze(
                    objectToApplyTo,
                    path,
                    ContractResolver,
                    operationToReport);

                patchObject.Add(value);
            }
            catch (JsonPatchException exception)
            {
                if (LogErrorAction != null)
                {
                    LogErrorAction(new JsonPatchError(exception.AffectedObject, exception.FailedOperation, exception.Message));
                }
                else
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// The "move" operation removes the value at a specified location and
        /// adds it to the target location.
        ///
        /// The operation object MUST contain a "from" member, which is a string
        /// containing a JSON Pointer value that references the location in the
        /// target document to move the value from.
        ///
        /// The "from" location MUST exist for the operation to be successful.
        ///
        /// For example:
        ///
        /// { "op": "move", "from": "/a/b/c", "path": "/a/b/d" }
        ///
        /// This operation is functionally identical to a "remove" operation on
        /// the "from" location, followed immediately by an "add" operation at
        /// the target location with the value that was just removed.
        ///
        /// The "from" location MUST NOT be a proper prefix of the "path"
        /// location; i.e., a location cannot be moved into one of its children.
        /// </summary>
        /// <param name="operation">The move operation.</param>
        /// <param name="objectToApplyTo">Object to apply the operation to.</param>
        public void Move(Operation operation, object objectToApplyTo)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            if (objectToApplyTo == null)
            {
                throw new ArgumentNullException(nameof(objectToApplyTo));
            }

            var valueAtFromLocationResult = GetValueAtLocation(operation.from, objectToApplyTo, operation);

            if (valueAtFromLocationResult.HasError)
            {
                // Error has already been logged in GetValueAtLocation.  We
                // must return, because remove / add should not be allowed to continue
                return;
            }

            // remove that value
            Remove(operation.from, objectToApplyTo, operation);

            // add that value to the path location
            Add(operation.path,
                valueAtFromLocationResult.PropertyValue,
                objectToApplyTo,
                operation);
        }

        /// <summary>
        /// The "remove" operation removes the value at the target location.
        ///
        /// The target location MUST exist for the operation to be successful.
        ///
        /// For example:
        ///
        /// { "op": "remove", "path": "/a/b/c" }
        ///
        /// If removing an element from an array, any elements above the
        /// specified index are shifted one position to the left.
        /// </summary>
        /// <param name="operation">The remove operation.</param>
        /// <param name="objectToApplyTo">Object to apply the operation to.</param>
        public void Remove(Operation operation, object objectToApplyTo)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            if (objectToApplyTo == null)
            {
                throw new ArgumentNullException(nameof(objectToApplyTo));
            }

            Remove(operation.path, objectToApplyTo, operation);
        }


        /// <summary>
        /// Remove is used by various operations (eg: remove, move, ...), yet through different operations;
        /// This method allows code reuse yet reporting the correct operation on error.  The return value
        /// contains the type of the item that has been removed (and a bool possibly signifying an error)
        /// This can be used by other methods, like replace, to ensure that we can pass in the correctly
        /// typed value to whatever method follows.
        /// </summary>
        private void Remove(string path, object objectToApplyTo, Operation operationToReport)
        {
            try
            {
                var patchObject = ObjectTreeAnalyzer.Analyze(
                    objectToApplyTo,
                    path,
                    ContractResolver,
                    operationToReport);

                patchObject.Remove();
            }
            catch (JsonPatchException exception)
            {
                if (LogErrorAction != null)
                {
                    LogErrorAction(new JsonPatchError(exception.AffectedObject, exception.FailedOperation, exception.Message));
                }
                else
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// The "replace" operation replaces the value at the target location
        /// with a new value.  The operation object MUST contain a "value" member
        /// whose content specifies the replacement value.
        ///
        /// The target location MUST exist for the operation to be successful.
        ///
        /// For example:
        ///
        /// { "op": "replace", "path": "/a/b/c", "value": 42 }
        ///
        /// This operation is functionally identical to a "remove" operation for
        /// a value, followed immediately by an "add" operation at the same
        /// location with the replacement value.
        ///
        /// Note: even though it's the same functionally, we do not call remove + add
        /// for performance reasons (multiple checks of same requirements).
        /// </summary>
        /// <param name="operation">The replace operation.</param>
        /// <param name="objectToApplyTo">Object to apply the operation to.</param>
        public void Replace(Operation operation, object objectToApplyTo)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            if (objectToApplyTo == null)
            {
                throw new ArgumentNullException(nameof(objectToApplyTo));
            }

            try
            {
                var patchObject = ObjectTreeAnalyzer.Analyze(
                    objectToApplyTo,
                    operation.path,
                    ContractResolver,
                    operation);

                patchObject.Replace(operation.value);
            }
            catch (JsonPatchException exception)
            {
                if (LogErrorAction != null)
                {
                    LogErrorAction(new JsonPatchError(exception.AffectedObject, exception.FailedOperation, exception.Message));
                }
                else
                {
                    throw;
                }
            }
        }

        /// <summary>
        ///  The "copy" operation copies the value at a specified location to the
        ///  target location.
        ///
        ///  The operation object MUST contain a "from" member, which is a string
        ///  containing a JSON Pointer value that references the location in the
        ///  target document to copy the value from.
        ///
        ///  The "from" location MUST exist for the operation to be successful.
        ///
        ///  For example:
        ///
        ///  { "op": "copy", "from": "/a/b/c", "path": "/a/b/e" }
        ///
        ///  This operation is functionally identical to an "add" operation at the
        ///  target location using the value specified in the "from" member.
        ///
        ///  Note: even though it's the same functionally, we do not call add with
        ///  the value specified in from for performance reasons (multiple checks of same requirements).
        /// </summary>
        /// <param name="operation">The copy operation.</param>
        /// <param name="objectToApplyTo">Object to apply the operation to.</param>
        public void Copy(Operation operation, object objectToApplyTo)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            if (objectToApplyTo == null)
            {
                throw new ArgumentNullException(nameof(objectToApplyTo));
            }

            // get value at from location and add that value to the path location
            var valueAtFromLocationResult = GetValueAtLocation(operation.from, objectToApplyTo, operation);

            if (valueAtFromLocationResult.HasError)
            {
                // Return, error has already been logged in GetValueAtLocation
                return;
            }

            Add(operation.path,
                valueAtFromLocationResult.PropertyValue,
                objectToApplyTo,
                operation);
        }

        /// <summary>
        /// Method is used by Copy and Move to avoid duplicate code
        /// </summary>
        /// <param name="location">Location where value should be</param>
        /// <param name="objectToGetValueFrom">Object to inspect for the desired value</param>
        /// <param name="operationToReport">Operation to report in case of an error</param>
        /// <returns>GetValueResult containing value and a bool signifying a possible error</returns>
        private GetValueResult GetValueAtLocation(
            string location,
            object objectToGetValueFrom,
            Operation operationToReport)
        {
            if (location == null)
            {
                throw new ArgumentNullException(nameof(location));
            }

            if (objectToGetValueFrom == null)
            {
                throw new ArgumentNullException(nameof(objectToGetValueFrom));
            }

            if (operationToReport == null)
            {
                throw new ArgumentNullException(nameof(operationToReport));
            }

            object value = null;
            var hasError = false;
            try
            {
                var patchObject = ObjectTreeAnalyzer.Analyze(
                    objectToGetValueFrom,
                    location,
                    ContractResolver,
                    operationToReport);

                value = patchObject.Get();
            }
            catch (JsonPatchException exception)
            {
                if (LogErrorAction != null)
                {
                    hasError = true;
                    LogErrorAction(new JsonPatchError(exception.AffectedObject, exception.FailedOperation, exception.Message));
                }
                else
                {
                    throw;
                }
            }

            return new GetValueResult(value, hasError);
        }
    }
}