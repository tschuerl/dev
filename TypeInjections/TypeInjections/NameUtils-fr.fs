// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: MIT

namespace TypeInjections

open System.Collections.Generic
open Microsoft.Boogie
open Microsoft.Dafny
open Utils
open YIL


// Utility functions to generate/get names for various parts of the type injection generation
module NameUtilsFR =

    // We want to get the name of a given type for a variety of purposes. We need to strip off the "Old" or "New"
    // from the full name.
    let typeName (t: Decl) : string = ""